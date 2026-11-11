using System.Data;
using CommonService.Application.Features.Payouts;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Payouts;

/// <summary>PAYOUT_BATCH and PAYOUT_ITEM over EF Core plus the one-table read of the flat JOB_ASSIGNMENT node (BE-M6-04, no navigation properties).</summary>
public class EfPayoutRepository(AppDbContext db) : IPayoutRepository
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

    public async Task LockPayeeAsync(PayeeKey payee, CancellationToken cancellationToken = default)
    {
        // The same resource as EfWithdrawalRepository, so a withdrawal approval and a settlement of one payee queue up.
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var kind = payee.Type == PayeeType.Freelancer ? "WORKER" : "AGENCY";
        var resource = new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = $"ledger-party:{kind}:{payee.Id}" };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000",
            [result, resource], cancellationToken);
        if ((int)result.Value < 0) throw new TimeoutException($"The wallet lock of {kind} {payee.Id} could not be taken in time.");
    }

    public async Task<int?> FindItemIdAsync(int batchId, PayeeKey payee, CancellationToken cancellationToken = default)
    {
        var query = db.PayoutItems.AsNoTracking().Where(i => i.BatchId == batchId && i.PayeeType == payee.Type);
        query = payee.Type == PayeeType.Freelancer ? query.Where(i => i.WorkerId == payee.Id) : query.Where(i => i.AgencyId == payee.Id);
        return await query.Select(i => (int?)i.ItemId).FirstOrDefaultAsync(cancellationToken);
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

    public async Task<int> AddItemAsync(int batchId, PayeeKey payee, string bankAccountNo, DateTime transferredAtUtc, CancellationToken cancellationToken = default)
    {
        var item = new PayoutItem
        {
            BatchId = batchId,
            PayeeType = payee.Type,
            WorkerId = payee.Type == PayeeType.Freelancer ? payee.Id : null,
            AgencyId = payee.Type == PayeeType.Agency ? payee.Id : null,
            BankAccountNo = bankAccountNo,
            ItemStatus = PayoutConstants.ItemTransferred,
            TransferredAt = transferredAtUtc,
        };
        db.PayoutItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        db.Entry(item).State = EntityState.Detached;
        return item.ItemId;
    }

    public async Task CompleteItemAsync(int itemId, NewPayoutItem figures, CancellationToken cancellationToken = default)
    {
        await db.PayoutItems.Where(i => i.ItemId == itemId).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.JobCount, figures.JobCount)
            .SetProperty(i => i.GrossAmount, figures.GrossAmount)
            .SetProperty(i => i.CommissionAmount, figures.CommissionAmount)
            .SetProperty(i => i.PenaltyAmount, figures.PenaltyAmount)
            .SetProperty(i => i.AdvanceAmount, figures.AdvanceAmount)
            .SetProperty(i => i.CarryInAmount, figures.CarryInAmount)
            .SetProperty(i => i.NetAmount, figures.NetAmount), cancellationToken);

        var ids = figures.AssignmentIds.ToList();
        if (ids.Count > 0)
        {
            await db.JobAssignments.Where(a => ids.Contains(a.AssignmentId) && a.PayoutItemId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.PayoutItemId, (int?)itemId), cancellationToken);
        }
    }

    public async Task<decimal> SumNetAsync(int batchId, CancellationToken cancellationToken = default) =>
        await db.PayoutItems.AsNoTracking().Where(i => i.BatchId == batchId).SumAsync(i => (decimal?)i.NetAmount, cancellationToken) ?? 0m;

    public async Task<(IReadOnlyList<PayoutBatch> Items, int Total)> ListBatchesAsync(int page, int pageSize, PayoutBatchSort? sort, CancellationToken cancellationToken = default)
    {
        var query = db.PayoutBatches.AsNoTracking();
        var total = await query.CountAsync(cancellationToken);
        var items = await Ordered(query, sort).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    /// <summary>The requested order, ties by the batch id in the same direction; no sort is newest month first.</summary>
    private IOrderedQueryable<PayoutBatch> Ordered(IQueryable<PayoutBatch> query, PayoutBatchSort? sort)
    {
        var desc = sort?.Descending ?? true;
        return (sort?.Key ?? PayoutBatchSortKey.Period) switch
        {
            PayoutBatchSortKey.Status => desc ? query.OrderByDescending(b => b.BatchStatus).ThenByDescending(b => b.BatchId) : query.OrderBy(b => b.BatchStatus).ThenBy(b => b.BatchId),
            PayoutBatchSortKey.Total => desc ? query.OrderByDescending(b => b.TotalAmount).ThenByDescending(b => b.BatchId) : query.OrderBy(b => b.TotalAmount).ThenBy(b => b.BatchId),
            PayoutBatchSortKey.ClosedAt => desc ? query.OrderByDescending(b => b.ConfirmedAt).ThenByDescending(b => b.BatchId) : query.OrderBy(b => b.ConfirmedAt).ThenBy(b => b.BatchId),
            PayoutBatchSortKey.Items => desc
                ? query.OrderByDescending(b => db.PayoutItems.Count(i => i.BatchId == b.BatchId)).ThenByDescending(b => b.BatchId)
                : query.OrderBy(b => db.PayoutItems.Count(i => i.BatchId == b.BatchId)).ThenBy(b => b.BatchId),
            _ => desc ? query.OrderByDescending(b => b.PeriodMonth).ThenByDescending(b => b.BatchId) : query.OrderBy(b => b.PeriodMonth).ThenBy(b => b.BatchId),
        };
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

    public async Task<IReadOnlyList<PayoutItemView>> GetAllItemsAsync(int batchId, PayeeType payeeType, CancellationToken cancellationToken = default)
    {
        var items = await db.PayoutItems.AsNoTracking()
            .Where(i => i.BatchId == batchId && i.PayeeType == payeeType)
            .OrderBy(i => i.ItemId)
            .ToListAsync(cancellationToken);
        var names = await NamesAsync(items, cancellationToken);
        return items.Select(i =>
        {
            names.TryGetValue(KeyOf(i.PayeeType, i.WorkerId, i.AgencyId), out var name);
            return new PayoutItemView(i, name.Name ?? string.Empty, name.Bank);
        }).ToList();
    }

    public async Task<IReadOnlyList<PayoutDetailRow>> GetAgencyDetailAsync(int batchId, CancellationToken cancellationToken = default)
    {
        var agencyItems = await db.PayoutItems.AsNoTracking()
            .Where(i => i.BatchId == batchId && i.PayeeType == PayeeType.Agency)
            .Select(i => new { i.ItemId, i.AgencyId })
            .ToListAsync(cancellationToken);
        if (agencyItems.Count == 0) return [];

        var itemIds = agencyItems.Select(i => (int?)i.ItemId).ToList();
        var agencyIds = agencyItems.Select(i => i.AgencyId!.Value).Distinct().ToList();
        var agencyNames = await db.PartnerAgencies.AsNoTracking()
            .Where(a => agencyIds.Contains(a.AgencyId)).Select(a => new { a.AgencyId, a.LegalName })
            .ToDictionaryAsync(a => a.AgencyId, a => a.LegalName, cancellationToken);

        var rows = await (
            from a in db.JobAssignments.AsNoTracking()
            join w in db.Workers.AsNoTracking() on a.WorkerId equals w.WorkerId
            where itemIds.Contains(a.PayoutItemId)
            select new
            {
                a.AssignmentId,
                a.OrderId,
                a.AgencyId,
                WorkerName = w.FullName,
                a.AssignmentStatus,
                a.GrossAmount,
                a.CommissionRate,
                a.AbsenceFeeAmount,
                a.CompletedAt,
                a.UpdatedAt,
            }).ToListAsync(cancellationToken);

        return rows
            .Select(r => new PayoutDetailRow(
                agencyNames.GetValueOrDefault(r.AgencyId ?? 0, string.Empty), r.AssignmentId, r.OrderId, r.WorkerName,
                r.AssignmentStatus, r.GrossAmount, r.CommissionRate, r.AbsenceFeeAmount,
                r.AssignmentStatus == JobAssignmentStatus.Absent ? r.UpdatedAt : r.CompletedAt ?? r.UpdatedAt))
            .OrderBy(r => r.AgencyName, StringComparer.Ordinal).ThenBy(r => r.AssignmentId)
            .ToList();
    }

    public Task SetExportUrlAsync(int batchId, string? url, CancellationToken cancellationToken = default) =>
        db.PayoutBatches.Where(b => b.BatchId == batchId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.ExportFileUrl, url), cancellationToken);

    public async Task<IReadOnlyList<string>> GetPayeesWithoutBankAsync(int batchId, CancellationToken cancellationToken = default)
    {
        var items = await db.PayoutItems.AsNoTracking()
            .Where(i => i.BatchId == batchId && i.BankAccountNo == string.Empty)
            .OrderBy(i => i.ItemId)
            .ToListAsync(cancellationToken);
        var names = await NamesAsync(items, cancellationToken);
        return items.Select(i => names.TryGetValue(KeyOf(i.PayeeType, i.WorkerId, i.AgencyId), out var n) ? n.Name : $"Payee {i.ItemId}").ToList();
    }

    public async Task<bool> TryCloseAsync(int batchId, decimal totalAmount, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var changed = await db.PayoutBatches
            .Where(b => b.BatchId == batchId && b.BatchStatus == PayoutConstants.BatchDraft)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.BatchStatus, PayoutConstants.BatchClosed)
                .SetProperty(b => b.ConfirmedBy, (int?)null) // closed by the system: nobody confirms a settlement
                .SetProperty(b => b.ConfirmedAt, (DateTime?)nowUtc)
                .SetProperty(b => b.TotalAmount, totalAmount), cancellationToken);
        return changed == 1;
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
