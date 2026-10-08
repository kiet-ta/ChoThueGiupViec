using CommonService.Application.Features.Payouts;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Payouts;

/// <summary>The freelancer's own income reads over <c>JOB_ASSIGNMENT</c>, <c>PAYOUT_BATCH</c> and <c>PAYOUT_ITEM</c>; every query is scoped to one worker id.</summary>
public sealed class EfWorkerEarningsRepository(AppDbContext db) : IWorkerEarningsRepository
{
    public async Task<WorkerType?> GetWorkerTypeAsync(int workerId, CancellationToken cancellationToken = default)
    {
        var types = await db.Workers.AsNoTracking().Where(w => w.WorkerId == workerId).Select(w => (WorkerType?)w.WorkerType).ToListAsync(cancellationToken);
        return types.SingleOrDefault();
    }

    public async Task<IReadOnlyList<WorkerEarningRow>> GetEarningsAsync(int workerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var rows = await db.JobAssignments.AsNoTracking()
            .Where(a => a.WorkerId == workerId && a.AgencyId == null)
            .Where(a =>
                (a.AssignmentStatus == JobAssignmentStatus.Completed && a.CompletedAt >= fromUtc && a.CompletedAt < toUtc)
                || (a.AssignmentStatus == JobAssignmentStatus.Absent && a.AbsenceFeeAmount > 0 && a.UpdatedAt >= fromUtc && a.UpdatedAt < toUtc))
            .Select(a => new { a.AssignmentId, a.OrderId, a.AssignmentStatus, a.GrossAmount, a.CommissionRate, a.AbsenceFeeAmount, a.CompletedAt, a.UpdatedAt })
            .ToListAsync(cancellationToken);

        return rows
            .Select(a => new WorkerEarningRow(
                a.AssignmentId, a.OrderId, a.AssignmentStatus, a.GrossAmount, a.CommissionRate, a.AbsenceFeeAmount,
                a.AssignmentStatus == JobAssignmentStatus.Absent ? a.UpdatedAt : a.CompletedAt ?? a.UpdatedAt))
            .OrderBy(r => r.DateUtc).ThenBy(r => r.AssignmentId)
            .ToList();
    }

    public async Task<WorkerBatchState?> GetBatchStateAsync(int workerId, string periodMonth, CancellationToken cancellationToken = default)
    {
        var batch = await db.PayoutBatches.AsNoTracking().FirstOrDefaultAsync(b => b.PeriodMonth == periodMonth, cancellationToken);
        if (batch is null) return null;

        var item = await db.PayoutItems.AsNoTracking()
            .Where(i => i.BatchId == batch.BatchId && i.PayeeType == PayeeType.Freelancer && i.WorkerId == workerId)
            .FirstOrDefaultAsync(cancellationToken);
        return new WorkerBatchState(
            batch.BatchId,
            batch.BatchStatus,
            item is null ? null : new WorkerBatchItem(item.JobCount, item.GrossAmount, item.CommissionAmount, item.PenaltyAmount, item.NetAmount));
    }

    public async Task<decimal> GetAppliedPenaltyBeforeAsync(int workerId, string periodMonth, CancellationToken cancellationToken = default)
    {
        var amounts = await (
            from i in db.PayoutItems.AsNoTracking()
            join b in db.PayoutBatches.AsNoTracking() on i.BatchId equals b.BatchId
            where b.BatchStatus == PayoutConstants.BatchClosed && b.PeriodMonth.CompareTo(periodMonth) < 0
                && i.PayeeType == PayeeType.Freelancer && i.WorkerId == workerId
            select i.PenaltyAmount).ToListAsync(cancellationToken);
        return amounts.Sum();
    }

    public async Task<(IReadOnlyList<WorkerPayoutHistoryRow> Items, int Total)> GetClosedItemsAsync(
        int workerId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query =
            from i in db.PayoutItems.AsNoTracking()
            join b in db.PayoutBatches.AsNoTracking() on i.BatchId equals b.BatchId
            where b.BatchStatus == PayoutConstants.BatchClosed && i.PayeeType == PayeeType.Freelancer && i.WorkerId == workerId
            select new { b.BatchId, b.PeriodMonth, i.NetAmount, i.ItemStatus, i.TransferredAt };

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(r => r.PeriodMonth).ThenByDescending(r => r.BatchId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (rows.Select(r => new WorkerPayoutHistoryRow(r.BatchId, r.PeriodMonth, r.NetAmount, r.ItemStatus, r.TransferredAt)).ToList(), total);
    }
}
