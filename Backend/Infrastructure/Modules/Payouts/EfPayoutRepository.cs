using System.Data;
using CommonService.Application.Features.Payouts;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Payouts;

/// <summary>PAYOUT_BATCH and PAYOUT_ITEM over EF Core plus the one-table read of the flat JOB_ASSIGNMENT node (BE-M6-04, no navigation properties).</summary>
public sealed class EfPayoutRepository(AppDbContext db) : IPayoutRepository
{
    public async Task<bool> TryLockPeriodAsync(string periodMonth, CancellationToken cancellationToken = default)
    {
        // sp_getapplock with LockOwner = 'Transaction' is released by the commit or the rollback of the caller's unit of work.
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var resource = new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = "payout-batch:" + periodMonth };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000",
            [result, resource], cancellationToken);
        return (int)result.Value >= 0;
    }

    public Task<PayoutBatch?> FindByPeriodAsync(string periodMonth, CancellationToken cancellationToken = default) =>
        db.PayoutBatches.AsNoTracking().FirstOrDefaultAsync(b => b.PeriodMonth == periodMonth, cancellationToken);

    public Task<PayoutBatch?> GetBatchAsync(int batchId, CancellationToken cancellationToken = default) =>
        db.PayoutBatches.AsNoTracking().FirstOrDefaultAsync(b => b.BatchId == batchId, cancellationToken);

    public async Task<PayoutBatch> AddDraftAsync(string periodMonth, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var batch = new PayoutBatch
        {
            PeriodMonth = periodMonth,
            BatchStatus = PayoutConstants.BatchDraft,
            TotalAmount = 0m,
            CreatedAt = nowUtc,
        };
        db.PayoutBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);
        db.Entry(batch).State = EntityState.Detached;
        return batch;
    }

    public async Task<IReadOnlyList<PayoutAssignmentRow>> GetPayableAsync(
        DateTime fromUtc, DateTime toUtc, int batchId, CancellationToken cancellationToken = default)
    {
        var ownItems = db.PayoutItems.Where(i => i.BatchId == batchId).Select(i => (int?)i.ItemId);

        return await db.JobAssignments.AsNoTracking()
            .Where(a => a.PayoutItemId == null || ownItems.Contains(a.PayoutItemId))
            .Where(a =>
                (a.AssignmentStatus == JobAssignmentStatus.Completed && a.CompletedAt >= fromUtc && a.CompletedAt < toUtc)
                || (a.AssignmentStatus == JobAssignmentStatus.Absent && a.AbsenceFeeAmount > 0 && a.UpdatedAt >= fromUtc && a.UpdatedAt < toUtc))
            .OrderBy(a => a.AssignmentId)
            .Select(a => new PayoutAssignmentRow(a.AssignmentId, a.WorkerId, a.AgencyId, a.AssignmentStatus, a.GrossAmount, a.CommissionRate, a.AbsenceFeeAmount))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<PayeeKey, decimal>> GetAppliedPenaltiesBeforeAsync(string periodMonth, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from i in db.PayoutItems.AsNoTracking()
            join b in db.PayoutBatches.AsNoTracking() on i.BatchId equals b.BatchId
            where b.BatchStatus == PayoutConstants.BatchClosed && b.PeriodMonth.CompareTo(periodMonth) < 0 && i.PenaltyAmount > 0
            select new { i.PayeeType, i.WorkerId, i.AgencyId, i.PenaltyAmount })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => KeyOf(r.PayeeType, r.WorkerId, r.AgencyId))
            .ToDictionary(g => g.Key, g => g.Sum(r => r.PenaltyAmount));
    }

    public async Task<IReadOnlyDictionary<PayeeKey, string>> GetBankAccountsAsync(
        IReadOnlyCollection<PayeeKey> payees, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<PayeeKey, string>();
        var workerIds = payees.Where(p => p.Type == PayeeType.Freelancer).Select(p => p.Id).ToList();
        var agencyIds = payees.Where(p => p.Type == PayeeType.Agency).Select(p => p.Id).ToList();

        if (workerIds.Count > 0)
        {
            var workers = await db.Workers.AsNoTracking().Where(w => workerIds.Contains(w.WorkerId))
                .Select(w => new { w.WorkerId, w.BankAccountNo }).ToListAsync(cancellationToken);
            foreach (var w in workers) result[new PayeeKey(PayeeType.Freelancer, w.WorkerId)] = w.BankAccountNo ?? string.Empty;
        }

        if (agencyIds.Count > 0)
        {
            var agencies = await db.PartnerAgencies.AsNoTracking().Where(a => agencyIds.Contains(a.AgencyId))
                .Select(a => new { a.AgencyId, a.BankAccountNo }).ToListAsync(cancellationToken);
            foreach (var a in agencies) result[new PayeeKey(PayeeType.Agency, a.AgencyId)] = a.BankAccountNo;
        }

        return result;
    }

    public async Task ReplaceItemsAsync(int batchId, IReadOnlyList<NewPayoutItem> items, decimal totalAmount, CancellationToken cancellationToken = default)
    {
        var oldItems = db.PayoutItems.Where(i => i.BatchId == batchId).Select(i => (int?)i.ItemId);
        await db.JobAssignments.Where(a => oldItems.Contains(a.PayoutItemId))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.PayoutItemId, (int?)null), cancellationToken);
        await db.PayoutItems.Where(i => i.BatchId == batchId).ExecuteDeleteAsync(cancellationToken);

        var entities = items.Select(n => new PayoutItem
        {
            BatchId = batchId,
            WorkerId = n.WorkerId,
            AgencyId = n.AgencyId,
            PayeeType = n.PayeeType,
            JobCount = n.JobCount,
            GrossAmount = n.GrossAmount,
            CommissionAmount = n.CommissionAmount,
            PenaltyAmount = n.PenaltyAmount,
            NetAmount = n.NetAmount,
            BankAccountNo = n.BankAccountNo,
            ItemStatus = PayoutConstants.ItemPending,
        }).ToList();
        db.PayoutItems.AddRange(entities);
        await db.SaveChangesAsync(cancellationToken);

        for (var i = 0; i < entities.Count; i++)
        {
            var ids = items[i].AssignmentIds.ToList();
            var itemId = entities[i].ItemId;
            var attached = await db.JobAssignments
                .Where(a => ids.Contains(a.AssignmentId) && a.PayoutItemId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.PayoutItemId, (int?)itemId), cancellationToken);
            if (attached != ids.Count) throw new PayoutClaimLostException(ids[0]);
        }

        await db.PayoutBatches.Where(b => b.BatchId == batchId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.TotalAmount, totalAmount), cancellationToken);
        db.ChangeTracker.Clear(); // the items were stored through the tracker; later reads must see the database
    }

    public async Task<(IReadOnlyList<PayoutBatch> Items, int Total)> ListBatchesAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = db.PayoutBatches.AsNoTracking();
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(b => b.PeriodMonth).ThenByDescending(b => b.BatchId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyDictionary<int, int>> CountItemsAsync(IReadOnlyCollection<int> batchIds, CancellationToken cancellationToken = default)
    {
        var ids = batchIds.ToList();
        var counts = await db.PayoutItems.AsNoTracking().Where(i => ids.Contains(i.BatchId))
            .GroupBy(i => i.BatchId).Select(g => new { BatchId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return counts.ToDictionary(c => c.BatchId, c => c.Count);
    }

    public async Task<(IReadOnlyList<PayoutItemView> Items, int Total)> GetItemsAsync(
        int batchId, PayeeType? payeeType, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = db.PayoutItems.AsNoTracking().Where(i => i.BatchId == batchId);
        if (payeeType is { } type) query = query.Where(i => i.PayeeType == type);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(i => i.ItemId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var names = await NamesAsync(items, cancellationToken);
        var views = items.Select(i =>
        {
            names.TryGetValue(KeyOf(i.PayeeType, i.WorkerId, i.AgencyId), out var name);
            return new PayoutItemView(i, name.Name ?? string.Empty, name.Bank);
        }).ToList();
        return (views, total);
    }

    public async Task<IReadOnlyList<string>> GetPayeesWithoutBankAsync(int batchId, CancellationToken cancellationToken = default)
    {
        var items = await db.PayoutItems.AsNoTracking()
            .Where(i => i.BatchId == batchId && i.BankAccountNo == string.Empty)
            .OrderBy(i => i.ItemId)
            .ToListAsync(cancellationToken);
        var names = await NamesAsync(items, cancellationToken);
        return items.Select(i => names.TryGetValue(KeyOf(i.PayeeType, i.WorkerId, i.AgencyId), out var n) ? n.Name : $"Payee {i.ItemId}").ToList();
    }

    public async Task<bool> TryCloseAsync(int batchId, int adminId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var changed = await db.PayoutBatches
            .Where(b => b.BatchId == batchId && b.BatchStatus == PayoutConstants.BatchDraft)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.BatchStatus, PayoutConstants.BatchClosed)
                .SetProperty(b => b.ConfirmedBy, (int?)adminId)
                .SetProperty(b => b.ConfirmedAt, (DateTime?)nowUtc), cancellationToken);
        if (changed == 0) return false;

        await db.PayoutItems.Where(i => i.BatchId == batchId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.ItemStatus, PayoutConstants.ItemTransferred)
                .SetProperty(i => i.TransferredAt, (DateTime?)nowUtc), cancellationToken);
        return true;
    }

    private static PayeeKey KeyOf(PayeeType type, int? workerId, int? agencyId) =>
        new(type, type == PayeeType.Freelancer ? workerId ?? 0 : agencyId ?? 0);

    /// <summary>Payee name and bank name of the freelancers and agencies of <paramref name="items"/>.</summary>
    private async Task<Dictionary<PayeeKey, (string Name, string? Bank)>> NamesAsync(IReadOnlyList<PayoutItem> items, CancellationToken cancellationToken)
    {
        var result = new Dictionary<PayeeKey, (string, string?)>();
        var workerIds = items.Where(i => i.PayeeType == PayeeType.Freelancer && i.WorkerId is not null).Select(i => i.WorkerId!.Value).Distinct().ToList();
        var agencyIds = items.Where(i => i.PayeeType == PayeeType.Agency && i.AgencyId is not null).Select(i => i.AgencyId!.Value).Distinct().ToList();

        if (workerIds.Count > 0)
        {
            var workers = await db.Workers.AsNoTracking().Where(w => workerIds.Contains(w.WorkerId))
                .Select(w => new { w.WorkerId, w.FullName, w.BankName }).ToListAsync(cancellationToken);
            foreach (var w in workers) result[new PayeeKey(PayeeType.Freelancer, w.WorkerId)] = (w.FullName, w.BankName);
        }

        if (agencyIds.Count > 0)
        {
            var agencies = await db.PartnerAgencies.AsNoTracking().Where(a => agencyIds.Contains(a.AgencyId))
                .Select(a => new { a.AgencyId, a.LegalName, a.BankName }).ToListAsync(cancellationToken);
            foreach (var a in agencies) result[new PayeeKey(PayeeType.Agency, a.AgencyId)] = (a.LegalName, a.BankName);
        }

        return result;
    }
}
