using CommonService.Application.Features.Disputes;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Disputes;

/// <summary>DISPUTE_TICKET over EF Core plus the read-only joins of the Admin console (no navigation properties, 0-JOIN style).</summary>
public sealed class EfDisputeRepository(AppDbContext db) : IDisputeRepository
{
    // SQL Server: 2601 = duplicate key in a unique index, 2627 = unique constraint violation.
    private static readonly int[] UniqueViolationNumbers = [2601, 2627];

    public async Task<DisputeOrderInfo?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var order = await db.JobOrders.AsNoTracking()
            .Where(o => o.OrderId == orderId)
            .Select(o => new { o.OrderId, o.OrderCode, o.CustomerId })
            .FirstOrDefaultAsync(cancellationToken);
        if (order is null) return null;

        var rows = await (
            from a in db.JobAssignments.AsNoTracking()
            join s in db.BookingSlots.AsNoTracking() on a.SlotId equals s.SlotId
            where a.OrderId == orderId
            select new { a.AssignmentId, a.WorkerId, a.AssignmentStatus, a.CompletedAt, s.SlotDate, s.EndTime })
            .ToListAsync(cancellationToken);

        var assignments = rows
            .Select(r => new DisputeAssignmentInfo(r.AssignmentId, r.WorkerId, r.AssignmentStatus, r.CompletedAt, r.SlotDate.ToDateTime(r.EndTime)))
            .ToList();
        return new DisputeOrderInfo(order.OrderId, order.OrderCode, order.CustomerId, assignments);
    }

    public async Task<bool> TryAddAsync(DisputeTicket ticket, CancellationToken cancellationToken = default)
    {
        db.DisputeTickets.Add(ticket);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && UniqueViolationNumbers.Contains(sql.Number))
        {
            db.Entry(ticket).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IReadOnlyList<DisputeTicket>> ListForCustomerAsync(int customerId, CancellationToken cancellationToken = default) =>
        await (from d in db.DisputeTickets.AsNoTracking()
               join o in db.JobOrders.AsNoTracking() on d.OrderId equals o.OrderId
               where o.CustomerId == customerId
               orderby d.CreatedAt descending, d.DisputeId descending
               select d).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DisputeTicket>> ListForWorkerAsync(int workerId, CancellationToken cancellationToken = default) =>
        await db.DisputeTickets.AsNoTracking()
            .Where(d => db.JobAssignments.Any(a => a.OrderId == d.OrderId && a.WorkerId == workerId))
            .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.DisputeId)
            .ToListAsync(cancellationToken);

    public Task<DisputeTicket?> GetForCustomerAsync(int disputeId, int customerId, CancellationToken cancellationToken = default) =>
        (from d in db.DisputeTickets.AsNoTracking()
         join o in db.JobOrders.AsNoTracking() on d.OrderId equals o.OrderId
         where d.DisputeId == disputeId && o.CustomerId == customerId
         select d).FirstOrDefaultAsync(cancellationToken);

    public Task<DisputeTicket?> GetForWorkerAsync(int disputeId, int workerId, CancellationToken cancellationToken = default) =>
        db.DisputeTickets.AsNoTracking()
            .Where(d => d.DisputeId == disputeId && db.JobAssignments.Any(a => a.OrderId == d.OrderId && a.WorkerId == workerId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<(IReadOnlyList<DisputeTicket> Items, int Total)> SearchAsync(
        DisputeQueueFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var statuses = filter.Statuses.ToList();
        var query = db.DisputeTickets.AsNoTracking().Where(d => statuses.Contains(d.DisputeStatus));
        if (filter.DueFromUtc is { } from) query = query.Where(d => d.SlaDueAt >= from);
        if (filter.DueBeforeUtc is { } before) query = query.Where(d => d.SlaDueAt < before);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(d => d.SlaDueAt).ThenBy(d => d.DisputeId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<DisputeTicket?> FindTrackedAsync(int disputeId, CancellationToken cancellationToken = default) =>
        db.DisputeTickets.FirstOrDefaultAsync(d => d.DisputeId == disputeId, cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<long, DisputeSummaryData>> GetSummaryDataAsync(
        IReadOnlyCollection<long> orderIds, CancellationToken cancellationToken = default)
    {
        var ids = orderIds.ToList();

        var orders = await (
            from o in db.JobOrders.AsNoTracking()
            join c in db.Customers.AsNoTracking() on o.CustomerId equals c.CustomerId
            where ids.Contains(o.OrderId)
            select new { o.OrderId, o.OrderCode, c.FullName })
            .ToListAsync(cancellationToken);

        var workers = await (
            from a in db.JobAssignments.AsNoTracking()
            join w in db.Workers.AsNoTracking() on a.WorkerId equals w.WorkerId
            join ag in db.PartnerAgencies.AsNoTracking() on w.AgencyId equals ag.AgencyId into agencies
            from ag in agencies.DefaultIfEmpty()
            where ids.Contains(a.OrderId)
            select new { a.OrderId, w.WorkerId, w.FullName, w.WorkerType, AgencyName = ag == null ? null : ag.LegalName })
            .ToListAsync(cancellationToken);

        return orders.ToDictionary(
            o => o.OrderId,
            o => new DisputeSummaryData(
                o.OrderId,
                o.OrderCode,
                o.FullName,
                workers.Where(w => w.OrderId == o.OrderId)
                    .DistinctBy(w => w.WorkerId)
                    .Select(w => new DisputeWorkerData(w.WorkerId, w.FullName, w.WorkerType, w.AgencyName))
                    .ToList()));
    }

    public async Task<DisputeCaseData> GetCaseDataAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var assignmentIds = await db.JobAssignments.AsNoTracking()
            .Where(a => a.OrderId == orderId)
            .Select(a => a.AssignmentId)
            .ToListAsync(cancellationToken);

        var checkIns = await db.CheckInLogs.AsNoTracking()
            .Where(c => assignmentIds.Contains(c.AssignmentId))
            .Select(c => new DisputeCheckInData(c.AssignmentId, c.CheckedInAt, c.GpsVerified, c.DistanceM))
            .ToListAsync(cancellationToken);

        var photos = await db.JobPhotos.AsNoTracking()
            .Where(p => assignmentIds.Contains(p.AssignmentId))
            .Select(p => new DisputePhotoData(p.AssignmentId, p.PhotoPhase, p.AngleNo, p.ImageUrl, p.VolScore, p.IsAccepted, p.CapturedAt))
            .ToListAsync(cancellationToken);

        var completions = (await db.JobAssignments.AsNoTracking()
                .Where(a => a.OrderId == orderId && a.CompletedAt != null)
                .Select(a => new { a.AssignmentId, CompletedAt = a.CompletedAt!.Value })
                .ToListAsync(cancellationToken))
            .Select(c => (c.AssignmentId, c.CompletedAt))
            .ToList();

        return new DisputeCaseData(checkIns, photos, completions);
    }
}
