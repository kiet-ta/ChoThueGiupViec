using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Ledger;

public class EfLedgerQuery(AppDbContext db) : ILedgerQuery
{
    public async Task<IReadOnlyList<LedgerEntry>> GetPartyEntriesAsync(LedgerPartyType type, int? id, DateTime? beforeUtc = null, CancellationToken cancellationToken = default)
    {
        var query = db.LedgerEntries.AsNoTracking().Where(e =>
            (e.ToPartyType == type && e.ToPartyId == id) || (e.FromPartyType == type && e.FromPartyId == id));
        if (beforeUtc.HasValue) query = query.Where(e => e.CreatedAt < beforeUtc.Value);
        return await query.OrderBy(e => e.EntryId).ToListAsync(cancellationToken);
    }

    public async Task<decimal> GetHeldAsync(int workerId, CancellationToken cancellationToken = default) =>
        await db.WithdrawalRequests.AsNoTracking().Where(w => w.WorkerId == workerId && w.RequestStatus == WithdrawalStatus.Pending)
            .SumAsync(w => (decimal?)w.Amount, cancellationToken) ?? 0m;

    public async Task<IReadOnlyList<(LedgerPartyType Type, int Id)>> GetSettlementPartiesAsync(DateTime beforeUtc, CancellationToken cancellationToken = default)
    {
        var received = db.LedgerEntries.AsNoTracking()
            .Where(e => e.CreatedAt < beforeUtc && (e.ToPartyType == LedgerPartyType.Worker || e.ToPartyType == LedgerPartyType.Agency) && e.ToPartyId != null)
            .Select(e => new { Type = e.ToPartyType, Id = e.ToPartyId!.Value });
        var sent = db.LedgerEntries.AsNoTracking()
            .Where(e => e.CreatedAt < beforeUtc && (e.FromPartyType == LedgerPartyType.Worker || e.FromPartyType == LedgerPartyType.Agency) && e.FromPartyId != null)
            .Select(e => new { Type = e.FromPartyType, Id = e.FromPartyId!.Value });
        var rows = await received.Union(sent).ToListAsync(cancellationToken);
        return rows.OrderBy(r => r.Type).ThenBy(r => r.Id).Select(r => (r.Type, r.Id)).ToList();
    }

    public async Task<IReadOnlyList<LedgerEntry>> GetCommissionsAsync(LedgerRefType refType, IReadOnlyCollection<long> refIds, CancellationToken cancellationToken = default)
    {
        var ids = refIds.ToList();
        return ids.Count == 0
            ? []
            : await db.LedgerEntries.AsNoTracking()
                .Where(e => e.EntryType == LedgerEntryType.Commission && e.RefType == refType && ids.Contains(e.RefId))
                .ToListAsync(cancellationToken);
    }

    public async Task<DateTime?> GetFirstEntryAtAsync(CancellationToken cancellationToken = default) =>
        await db.LedgerEntries.AsNoTracking().OrderBy(e => e.EntryId).Select(e => (DateTime?)e.CreatedAt).FirstOrDefaultAsync(cancellationToken);
}
