using CommonService.Application.Features.Ledger;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Ledger;

/// <summary>Read-only (AsNoTracking) history and totals over <c>LEDGER_ENTRY</c>; the sums are done by the database.</summary>
public class EfLedgerHistoryQuery(AppDbContext db) : ILedgerHistoryQuery
{
    public async Task<(IReadOnlyList<LedgerEntry> Items, int Total)> SearchAsync(
        LedgerSearch search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = db.LedgerEntries.AsNoTracking().AsQueryable();
        if (search.FromUtc is { } from) query = query.Where(e => e.CreatedAt >= from);
        if (search.ToExclusiveUtc is { } to) query = query.Where(e => e.CreatedAt < to);
        if (search.Type is { } type) query = query.Where(e => e.EntryType == type);

        if (search.Parties.Count > 0)
        {
            // One or two accounts (an agency's wallet and escrow); an id of null with AnyId false is the platform's own account.
            var first = search.Parties[0];
            var second = search.Parties.Count > 1 ? search.Parties[1] : null;
            var (t1, id1, any1) = (first.Type, first.Id, first.AnyId);
            var (has2, t2, id2, any2) = (second is not null, second?.Type ?? first.Type, second?.Id, second?.AnyId ?? false);
            query = query.Where(e =>
                (e.ToPartyType == t1 && (any1 || e.ToPartyId == id1)) || (e.FromPartyType == t1 && (any1 || e.FromPartyId == id1)) ||
                (has2 && ((e.ToPartyType == t2 && (any2 || e.ToPartyId == id2)) || (e.FromPartyType == t2 && (any2 || e.FromPartyId == id2)))));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(e => e.EntryId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<decimal> GetBalanceBeforeAsync(LedgerParty party, DateTime? beforeUtc, CancellationToken cancellationToken = default)
    {
        var (type, id) = (party.Type, party.Id);
        var incoming = db.LedgerEntries.AsNoTracking().Where(e => e.ToPartyType == type && e.ToPartyId == id);
        var outgoing = db.LedgerEntries.AsNoTracking().Where(e => e.FromPartyType == type && e.FromPartyId == id);
        if (beforeUtc is { } before)
        {
            incoming = incoming.Where(e => e.CreatedAt < before);
            outgoing = outgoing.Where(e => e.CreatedAt < before);
        }

        return (await incoming.SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m)
            - (await outgoing.SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m);
    }

    public async Task<LedgerTotals> GetTotalsAsync(CancellationToken cancellationToken = default)
    {
        async Task<decimal> NetAsync(LedgerPartyType type, bool platform)
        {
            var incoming = db.LedgerEntries.AsNoTracking().Where(e => e.ToPartyType == type && (!platform || e.ToPartyId == null));
            var outgoing = db.LedgerEntries.AsNoTracking().Where(e => e.FromPartyType == type && (!platform || e.FromPartyId == null));
            return (await incoming.SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m)
                - (await outgoing.SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m);
        }

        var held = await db.WithdrawalRequests.AsNoTracking().Where(w => w.RequestStatus == WithdrawalStatus.Pending)
            .SumAsync(w => (decimal?)w.Amount, cancellationToken) ?? 0m;
        return new LedgerTotals(
            await NetAsync(LedgerPartyType.PlatformRevenue, true), await NetAsync(LedgerPartyType.PlatformClearing, true),
            await NetAsync(LedgerPartyType.Worker, false), await NetAsync(LedgerPartyType.Agency, false), held);
    }

    public Task<LedgerEntry?> GetByKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
        db.LedgerEntries.AsNoTracking().FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey, cancellationToken);
}
