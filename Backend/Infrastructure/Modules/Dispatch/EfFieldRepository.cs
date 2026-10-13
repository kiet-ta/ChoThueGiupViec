using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Dispatch;

/// <summary>
/// EF Core persistence for the worker's field steps: check-in (<see cref="IFieldCheckInRepository"/>), call attempts and customer absence
/// (<see cref="ICustomerAbsentRepository"/>), and the offer/address reads of <see cref="IWorkerDispatchQuery"/>. Status changes are
/// conditional updates. Not verified against a live SQL Server on the Linux dev host.
/// </summary>
public sealed class EfFieldRepository(AppDbContext db) : IFieldCheckInRepository, ICustomerAbsentRepository, IWorkerDispatchQuery
{
    public async Task<JobAssignment?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default) =>
        await db.JobAssignments.AsNoTracking().FirstOrDefaultAsync(a => a.AssignmentId == assignmentId, cancellationToken);

    public async Task<CheckInLog?> GetCheckInLogAsync(long assignmentId, CancellationToken cancellationToken = default) =>
        await db.CheckInLogs.AsNoTracking().FirstOrDefaultAsync(l => l.AssignmentId == assignmentId, cancellationToken);

    /// <summary>Inserts the assignment's log, or updates it (<c>CHECK_IN_LOG.assignment_id</c> is unique).</summary>
    public async Task<CheckInLog> SaveCheckInLogAsync(CheckInLog log, CancellationToken cancellationToken = default)
    {
        if (log.CheckinId == 0)
        {
            db.CheckInLogs.Add(log);
        }
        else
        {
            db.CheckInLogs.Update(log);
        }

        await db.SaveChangesAsync(cancellationToken);
        db.Entry(log).State = EntityState.Detached;
        return log;
    }

    public async Task<bool> TransitionToCheckedInAsync(long assignmentId, DateTime checkedInAtUtc, CancellationToken cancellationToken = default) =>
        await db.JobAssignments
            .Where(a => a.AssignmentId == assignmentId && a.AssignmentStatus == JobAssignmentStatus.Assigned)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.AssignmentStatus, JobAssignmentStatus.CheckedIn)
                .SetProperty(a => a.UpdatedAt, checkedInAtUtc), cancellationToken) == 1;

    public async Task<bool> RecordCallAttemptAsync(long assignmentId, CancellationToken cancellationToken = default) =>
        await db.CheckInLogs
            .Where(l => l.AssignmentId == assignmentId && l.CallAttempts < byte.MaxValue)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.CallAttempts, l => (byte)(l.CallAttempts + 1)), cancellationToken) == 1;

    public async Task<bool> UpdateAssignmentAbsentFeeAsync(long assignmentId, decimal absenceFeeAmount, CancellationToken cancellationToken = default) =>
        await db.JobAssignments
            .Where(a => a.AssignmentId == assignmentId && a.AssignmentStatus == JobAssignmentStatus.CheckedIn)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AbsenceFeeAmount, (decimal?)absenceFeeAmount), cancellationToken) == 1;

    public async Task<OfferView?> GetOfferViewAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        var row = await (
            from a in db.JobAssignments.AsNoTracking()
            join o in db.JobOrders.AsNoTracking() on a.OrderId equals o.OrderId
            join addr in db.CustomerAddresses.AsNoTracking() on o.AddressId equals addr.AddressId
            join slot in db.BookingSlots.AsNoTracking() on a.SlotId equals slot.SlotId
            join w in db.Workers.AsNoTracking() on a.WorkerId equals w.WorkerId
            where a.AssignmentId == assignmentId
            select new
            {
                Assignment = a,
                o.ScheduledDate,
                slot.StartTime,
                slot.EndTime,
                addr.District,
                addr.Latitude,
                addr.Longitude,
                w.CurrentLat,
                w.CurrentLng,
            }
        ).FirstOrDefaultAsync(cancellationToken);

        if (row == null)
        {
            return null;
        }

        GeoPoint? workerAt = row.CurrentLat is { } lat && row.CurrentLng is { } lng ? new GeoPoint((double)lat, (double)lng) : null;
        return new OfferView(row.Assignment, row.ScheduledDate, row.StartTime, row.EndTime, row.District,
            new GeoPoint((double)row.Latitude, (double)row.Longitude), workerAt);
    }

    public async Task<GeoPoint?> GetOrderLocationOfAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        var at = await (
            from a in db.JobAssignments.AsNoTracking()
            join o in db.JobOrders.AsNoTracking() on a.OrderId equals o.OrderId
            join addr in db.CustomerAddresses.AsNoTracking() on o.AddressId equals addr.AddressId
            where a.AssignmentId == assignmentId
            select new { addr.Latitude, addr.Longitude }
        ).FirstOrDefaultAsync(cancellationToken);

        return at == null ? null : new GeoPoint((double)at.Latitude, (double)at.Longitude);
    }
}
