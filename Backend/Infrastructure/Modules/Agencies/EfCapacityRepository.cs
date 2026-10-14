using CommonService.Application.Common.Options;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>
/// EF Core implementation of <see cref="ICapacityRepository"/>. A hold is made by conditional updates in one transaction
/// (<c>AVAILABLE -&gt; HELD</c> per slot, then the hold rows, whose unique index on <c>slot_id</c> is the second guard), so two orders can
/// never both hold a slot. Not verified against a live SQL Server on this host (no server); covered by the owner's Windows run.
/// </summary>
public sealed class EfCapacityRepository(AppDbContext db, IClock clock, IOptions<BusinessRules> rules) : ICapacityRepository
{
    public async Task<IReadOnlyList<CapacitySlot>> GetOpenSlotsAsync(
        DateOnly date, string shiftCode, decimal minSlaScore, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from slot in db.BookingSlots.AsNoTracking()
            join agency in db.PartnerAgencies.AsNoTracking() on slot.AgencyId equals agency.AgencyId
            join worker in db.Workers.AsNoTracking() on slot.WorkerId equals worker.WorkerId
            where slot.SlotDate == date
                && slot.ShiftCode == shiftCode
                && slot.SlotStatus == DispatchSlotStatuses.Available
                && worker.WorkStatus != WorkStatus.Locked // a worker locked by the quota (or any lock) takes no new Premium job (D16)
                && agency.AgencyStatus == AgencyStatuses.Active
                && agency.SlaScore >= minSlaScore
                && !db.AgencyCapacityHolds.Any(h => h.SlotId == slot.SlotId)
            select new { slot.SlotId, agency.AgencyId, agency.SlaScore }
        ).ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var packages = await ActivePackagesAsync(rows.Select(r => r.AgencyId).Distinct().ToList(), cancellationToken);
        return rows
            .Select(r => packages.TryGetValue(r.AgencyId, out var package)
                ? new CapacitySlot(r.SlotId, r.AgencyId, r.SlaScore, package.PriorityDispatch, package.CommissionRate)
                : new CapacitySlot(r.SlotId, r.AgencyId, r.SlaScore, PriorityDispatch: false, CapacityPlanner.FreeCommissionRate))
            .ToList();
    }

    public async Task<bool> TryHoldAsync(
        long orderId, Guid reservationId, int agencyId, IReadOnlyList<int> slotIds, decimal commissionRate, DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // Booking creates the order and its hold in one unit of work: join that transaction when there is one (a savepoint keeps
            // a lost race from undoing the order) and own a transaction otherwise.
            var ambient = db.Database.CurrentTransaction;
            var transaction = ambient ?? await db.Database.BeginTransactionAsync(cancellationToken);
            const string savepoint = "agency_capacity_hold";
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                foreach (var slotId in slotIds)
                {
                    var held = await db.BookingSlots
                        .Where(s => s.SlotId == slotId && s.AgencyId == agencyId && s.SlotStatus == DispatchSlotStatuses.Available)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.SlotStatus, DispatchSlotStatuses.Held)
                            .SetProperty(x => x.UpdatedAt, nowUtc), cancellationToken);
                    if (held != 1)
                    {
                        await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
                        return false;
                    }
                }

                foreach (var slotId in slotIds)
                {
                    db.AgencyCapacityHolds.Add(new AgencyCapacityHold
                    {
                        ReservationId = reservationId,
                        OrderId = orderId,
                        AgencyId = agencyId,
                        SlotId = slotId,
                        CommissionRate = commissionRate,
                        CreatedAt = nowUtc,
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
                if (ambient is null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return true;
            }
            catch (DbUpdateException ex) when (SqlUniqueViolation.IsUniqueViolation(ex))
            {
                // Another order already holds one of the slots (unique index on slot_id): nothing is kept.
                await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
                foreach (var entry in db.ChangeTracker.Entries<AgencyCapacityHold>().ToList())
                {
                    entry.State = EntityState.Detached;
                }

                return false;
            }
            finally
            {
                if (ambient is null)
                {
                    await transaction.DisposeAsync();
                }
            }
        });
    }

    public async Task<StoredCapacityHold?> GetByOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var rows = await db.AgencyCapacityHolds.AsNoTracking()
            .Where(h => h.OrderId == orderId)
            .OrderBy(h => h.SlotId)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return null;
        }

        var first = rows[0];
        return new StoredCapacityHold(
            first.ReservationId, orderId, first.AgencyId, rows.Select(r => r.SlotId).ToList(), first.CommissionRate);
    }

    public Task ReleaseByOrderAsync(long orderId, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        ReleaseAsync(h => h.OrderId == orderId, nowUtc, cancellationToken);

    public Task ReleaseAsync(Guid reservationId, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        ReleaseAsync(h => h.ReservationId == reservationId, nowUtc, cancellationToken);

    private async Task ReleaseAsync(
        System.Linq.Expressions.Expression<Func<AgencyCapacityHold, bool>> which, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            var ambient = db.Database.CurrentTransaction;
            var transaction = ambient ?? await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var slotIds = await db.AgencyCapacityHolds.Where(which).Select(h => h.SlotId).ToListAsync(cancellationToken);
                if (slotIds.Count > 0)
                {
                    // Only a slot that is still HELD goes back: one that became BOOKED belongs to an assignment (Dispatch frees it).
                    await db.BookingSlots
                        .Where(s => slotIds.Contains(s.SlotId) && s.SlotStatus == DispatchSlotStatuses.Held)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.SlotStatus, DispatchSlotStatuses.Available)
                            .SetProperty(x => x.UpdatedAt, nowUtc), cancellationToken);
                    await db.AgencyCapacityHolds.Where(which).ExecuteDeleteAsync(cancellationToken);
                }

                if (ambient is null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            }
            finally
            {
                if (ambient is null)
                {
                    await transaction.DisposeAsync();
                }
            }
        });
    }

    private async Task<Dictionary<int, (bool PriorityDispatch, decimal CommissionRate)>> ActivePackagesAsync(
        IReadOnlyCollection<int> agencyIds, CancellationToken cancellationToken)
    {
        var today = clock.LocalToday;
        var graceDays = rules.Value.Subscription.GraceDays;
        var latestEnd = today.AddDays(-graceDays);

        // An active subscription counts until its end date plus the grace days (decisions Q08); the newest one wins.
        var rows = await (
            from sub in db.PartnerSubscriptions.AsNoTracking()
            join package in db.SubscriptionPackages.AsNoTracking() on sub.PackageId equals package.PackageId
            where agencyIds.Contains(sub.AgencyId)
                && sub.SubStatus == SubscriptionStatuses.Active
                && sub.StartDate <= today
                && sub.EndDate >= latestEnd
            orderby sub.EndDate descending
            select new { sub.AgencyId, package.PriorityDispatch, package.CommissionRate }
        ).ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.AgencyId)
            .ToDictionary(g => g.Key, g => (g.First().PriorityDispatch, g.First().CommissionRate));
    }
}
