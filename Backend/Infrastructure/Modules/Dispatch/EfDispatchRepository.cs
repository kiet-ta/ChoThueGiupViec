using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Dispatch;

/// <summary>
/// EF Core implementation of <see cref="IDispatchRepository"/>. Offers are <c>JOB_ASSIGNMENT</c> rows in <c>OFFERED</c>; every state change
/// is a conditional update (or an insert guarded by one) inside a transaction, so concurrent requests cannot both win.
/// Not verified against a live SQL Server on the Linux dev host (no server there); covered by the owner's Windows run.
/// </summary>
public sealed class EfDispatchRepository(AppDbContext db) : IDispatchRepository
{
    public async Task<DispatchOrderContext?> GetOrderContextAsync(long orderId, CancellationToken cancellationToken = default) =>
        await (
            from order in db.JobOrders.AsNoTracking()
            join address in db.CustomerAddresses.AsNoTracking() on order.AddressId equals address.AddressId
            where order.OrderId == orderId
            select new DispatchOrderContext(
                order.OrderId,
                order.CustomerId,
                order.ServiceTier,
                order.ScheduledDate,
                order.ShiftCode,
                order.RequiredWorkers,
                new GeoPoint((double)address.Latitude, (double)address.Longitude),
                order.TotalAmount)
        ).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<long>> GetWorkerIdsAlreadyOfferedAsync(long orderId, CancellationToken cancellationToken = default) =>
        await db.JobAssignments.AsNoTracking()
            .Where(a => a.OrderId == orderId)
            .Select(a => (long)a.WorkerId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public Task<JobAssignment?> TryCreateOfferAsync(JobAssignment offer, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            var held = await db.BookingSlots
                .Where(s => s.SlotId == offer.SlotId && s.SlotStatus == DispatchSlotStatuses.Available)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.SlotStatus, DispatchSlotStatuses.Held)
                    .SetProperty(x => x.UpdatedAt, offer.CreatedAt), cancellationToken);
            if (held != 1)
            {
                return null;
            }

            db.JobAssignments.Add(offer);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // The filtered unique index on slot_id: an open assignment already owns the slot. Nothing is kept.
                db.Entry(offer).State = EntityState.Detached;
                return null;
            }

            return offer;
        }, cancellationToken);

    public async Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default) =>
        await db.JobAssignments.AsNoTracking().FirstOrDefaultAsync(a => a.AssignmentId == assignmentId, cancellationToken);

    public async Task<JobAssignment?> GetOpenOfferForWorkerAsync(int workerId, DateTime offeredAfterUtc, CancellationToken cancellationToken = default) =>
        await db.JobAssignments.AsNoTracking()
            .Where(a => a.WorkerId == workerId && a.AssignmentStatus == JobAssignmentStatus.Offered && a.CreatedAt > offeredAfterUtc)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TryAcceptOfferAsync(
        long assignmentId,
        int workerId,
        DateTime offeredAfterUtc,
        DateTime acceptedAtUtc,
        CancellationToken cancellationToken = default) =>
        await InTransactionAsync(async () =>
        {
            var slotId = await db.JobAssignments.AsNoTracking()
                .Where(a => a.AssignmentId == assignmentId)
                .Select(a => (int?)a.SlotId)
                .FirstOrDefaultAsync(cancellationToken);
            if (slotId == null)
            {
                return false;
            }

            var accepted = await db.JobAssignments
                .Where(a => a.AssignmentId == assignmentId
                    && a.WorkerId == workerId
                    && a.AssignmentStatus == JobAssignmentStatus.Offered
                    && a.CreatedAt > offeredAfterUtc)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.AssignmentStatus, JobAssignmentStatus.Assigned)
                    .SetProperty(a => a.AcceptedAt, (DateTime?)acceptedAtUtc)
                    .SetProperty(a => a.UpdatedAt, acceptedAtUtc), cancellationToken);
            if (accepted != 1)
            {
                return false;
            }

            var booked = await db.BookingSlots
                .Where(s => s.SlotId == slotId.Value && s.SlotStatus == DispatchSlotStatuses.Held)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.SlotStatus, DispatchSlotStatuses.Booked)
                    .SetProperty(x => x.UpdatedAt, acceptedAtUtc), cancellationToken);
            if (booked != 1)
            {
                throw new SlotNotHeldException(); // rolls the acceptance back
            }

            return true;
        }, cancellationToken, rollbackOn: typeof(SlotNotHeldException));

    public Task<bool> TryCloseOfferAsync(long assignmentId, DateTime closedAtUtc, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            var offer = await db.JobAssignments.AsNoTracking()
                .Where(a => a.AssignmentId == assignmentId)
                .Select(a => new { a.SlotId })
                .FirstOrDefaultAsync(cancellationToken);
            if (offer == null)
            {
                return false;
            }

            var nowUtc = closedAtUtc;
            var closed = await db.JobAssignments
                .Where(a => a.AssignmentId == assignmentId && a.AssignmentStatus == JobAssignmentStatus.Offered)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.AssignmentStatus, JobAssignmentStatus.Cancelled)
                    .SetProperty(a => a.UpdatedAt, nowUtc), cancellationToken);
            if (closed != 1)
            {
                return false;
            }

            await db.BookingSlots
                .Where(s => s.SlotId == offer.SlotId && s.SlotStatus == DispatchSlotStatuses.Held)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.SlotStatus, DispatchSlotStatuses.Available)
                    .SetProperty(x => x.UpdatedAt, nowUtc), cancellationToken);
            return true;
        }, cancellationToken);

    public async Task<IReadOnlyList<JobAssignment>> GetOffersCreatedAtOrBeforeAsync(DateTime createdAtOrBeforeUtc, CancellationToken cancellationToken = default) =>
        await db.JobAssignments.AsNoTracking()
            .Where(a => a.AssignmentStatus == JobAssignmentStatus.Offered && a.CreatedAt <= createdAtOrBeforeUtc)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<int?> GetSlotWorkerIdAsync(int slotId, CancellationToken cancellationToken = default) =>
        await db.BookingSlots.AsNoTracking()
            .Where(s => s.SlotId == slotId)
            .Select(s => (int?)s.WorkerId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<JobAssignment> CreateAssignedAsync(JobAssignment assignment, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            assignment.TransitionTo(JobAssignmentStatus.Assigned);
            db.JobAssignments.Add(assignment);
            await db.SaveChangesAsync(cancellationToken);

            await db.BookingSlots
                .Where(s => s.SlotId == assignment.SlotId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.SlotStatus, DispatchSlotStatuses.Booked)
                    .SetProperty(x => x.UpdatedAt, assignment.CreatedAt), cancellationToken);
            return assignment;
        }, cancellationToken);

    /// <summary>
    /// Runs <paramref name="action"/> in a transaction; a null/false result rolls back. <paramref name="rollbackOn"/> also rolls back and
    /// is reported as a false result instead of an exception.
    /// </summary>
    private async Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken, Type? rollbackOn = null)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var result = await action();
                if (result is null || result is false)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                else
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return result;
            }
            catch (Exception ex) when (rollbackOn != null && rollbackOn.IsInstanceOfType(ex))
            {
                await transaction.RollbackAsync(cancellationToken);
                return (T)(object)false;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    private sealed class SlotNotHeldException : Exception;
}
