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

    public async Task<DateTime?> GetFirstEntryAtAsync(CancellationToken cancellationToken = default) =>
        await db.LedgerEntries.AsNoTracking().OrderBy(e => e.EntryId).Select(e => (DateTime?)e.CreatedAt).FirstOrDefaultAsync(cancellationToken);
}
