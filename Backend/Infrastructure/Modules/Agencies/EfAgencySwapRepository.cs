using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Dispatch;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>EF Core implementation of <see cref="IAgencySwapRepository"/>. Not verified against a live SQL Server on this host.</summary>
public sealed class EfAgencySwapRepository(AppDbContext db) : IAgencySwapRepository
{
    public async Task<SwapAssignmentView?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        var row = await (
            from a in db.JobAssignments.AsNoTracking()
            join s in db.BookingSlots.AsNoTracking() on a.SlotId equals s.SlotId
            where a.AssignmentId == assignmentId && a.AgencyId != null
            select new
            {
                a.AssignmentId,
                a.OrderId,
                AgencyId = a.AgencyId!.Value,
                a.WorkerId,
                a.SlotId,
                a.AssignmentStatus,
                s.SlotDate,
                s.ShiftCode,
                s.StartTime,
                a.ServiceTier,
            }
        ).FirstOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new SwapAssignmentView(
                row.AssignmentId, row.OrderId, row.AgencyId, row.WorkerId, row.SlotId, row.AssignmentStatus,
                row.SlotDate, row.ShiftCode, row.StartTime, row.ServiceTier);
    }

    public async Task<AgencySwapWindow?> GetWindowAsync(long assignmentId, CancellationToken cancellationToken = default) =>
        await db.AgencySwapWindows.AsNoTracking().FirstOrDefaultAsync(w => w.AssignmentId == assignmentId, cancellationToken);

    public void AddWindow(AgencySwapWindow window) => db.AgencySwapWindows.Add(window);

    public async Task<bool> TryCloseWindowAsync(long assignmentId, string to, int? newWorkerId, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        await db.AgencySwapWindows
            .Where(w => w.AssignmentId == assignmentId && w.Status == SwapWindowStatuses.Open)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Status, to)
                .SetProperty(w => w.NewWorkerId, newWorkerId)
                .SetProperty(w => w.ClosedAt, (DateTime?)nowUtc), cancellationToken) == 1;

    public async Task<IReadOnlyList<AgencySwapWindow>> ListOpenAsync(int? agencyId, CancellationToken cancellationToken = default) =>
        await db.AgencySwapWindows.AsNoTracking()
            .Where(w => w.Status == SwapWindowStatuses.Open && (agencyId == null || w.AgencyId == agencyId))
            .OrderBy(w => w.DeadlineAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AgencySwapWindow>> ListOpenDueAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default) =>
        await db.AgencySwapWindows.AsNoTracking()
            .Where(w => w.Status == SwapWindowStatuses.Open && w.DeadlineAt <= nowUtc)
            .OrderBy(w => w.DeadlineAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<WorkStatus?> GetAgencyWorkerStatusAsync(int agencyId, int workerId, CancellationToken cancellationToken = default) =>
        await db.Workers.AsNoTracking()
            .Where(w => w.WorkerId == workerId && w.AgencyId == agencyId)
            .Select(w => (WorkStatus?)w.WorkStatus)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<BookingSlot?> GetAvailableSlotAsync(int workerId, DateOnly date, string shiftCode, CancellationToken cancellationToken = default) =>
        await db.BookingSlots.AsNoTracking()
            .Where(s => s.WorkerId == workerId && s.SlotDate == date && s.ShiftCode == shiftCode && s.SlotStatus == DispatchSlotStatuses.Available)
            .OrderBy(s => s.SlotId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TrySwapAsync(
        long assignmentId, int oldSlotId, int newWorkerId, int newSlotId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var booked = await db.BookingSlots
            .Where(s => s.SlotId == newSlotId && s.WorkerId == newWorkerId && s.SlotStatus == DispatchSlotStatuses.Available)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.SlotStatus, DispatchSlotStatuses.Booked)
                .SetProperty(x => x.UpdatedAt, nowUtc), cancellationToken);
        if (booked != 1)
        {
            return false;
        }

        var moved = await db.JobAssignments
            .Where(a => a.AssignmentId == assignmentId && a.SlotId == oldSlotId && a.AssignmentStatus == JobAssignmentStatus.Assigned)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.WorkerId, newWorkerId)
                .SetProperty(a => a.SlotId, newSlotId)
                .SetProperty(a => a.UpdatedAt, nowUtc), cancellationToken);
        if (moved != 1)
        {
            // Thrown (not returned) so the unit of work rolls back and the new slot is not left BOOKED.
            throw new BusinessRuleViolationException("The assignment changed; try again.", SwapErrorCodes.SlotTaken);
        }

        await db.BookingSlots
            .Where(s => s.SlotId == oldSlotId && s.SlotStatus == DispatchSlotStatuses.Booked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.SlotStatus, DispatchSlotStatuses.Available)
                .SetProperty(x => x.UpdatedAt, nowUtc), cancellationToken);
        return true;
    }

}
