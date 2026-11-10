using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Ledger;

public record LedgerBalance(decimal Balance, decimal? Available, decimal Held, DateTime? NextAvailableAt);

public interface ILedgerBalanceService
{
    Task<decimal> BalanceBeforeAsync(LedgerParty party, DateTime? beforeUtc = null, CancellationToken cancellationToken = default);
    Task<LedgerBalance> GetAsync(LedgerParty party, CancellationToken cancellationToken = default);
}

public class LedgerBalanceService(ILedgerQuery ledger, IOpenDisputeQuery disputes, IClock clock) : ILedgerBalanceService
{
    public async Task<decimal> BalanceBeforeAsync(LedgerParty party, DateTime? beforeUtc = null, CancellationToken cancellationToken = default)
    {
        var entries = await ledger.GetPartyEntriesAsync(party.Type, party.Id, beforeUtc, cancellationToken);
        return Net(entries, party);
    }

    public async Task<LedgerBalance> GetAsync(LedgerParty party, CancellationToken cancellationToken = default)
    {
        var entries = await ledger.GetPartyEntriesAsync(party.Type, party.Id, null, cancellationToken);
        var balance = Net(entries, party);
        if (party.Type != LedgerPartyType.Worker || party.Id is null)
            return new LedgerBalance(balance, null, 0m, null);

        var held = await ledger.GetHeldAsync(party.Id.Value, cancellationToken);
        var credits = entries.Where(e => e.ToPartyType == party.Type && e.ToPartyId == party.Id).ToArray();
        var orderIds = credits.Where(e => e.OrderId.HasValue).Select(e => e.OrderId!.Value).Distinct().ToArray();
        var openOrders = await disputes.GetOpenOrderIdsAsync(orderIds, cancellationToken);
        var eligible = credits.Where(e => !e.OrderId.HasValue || !openOrders.Contains(e.OrderId.Value)).ToArray();
        var now = clock.UtcNow;
        var mature = eligible.Where(e => e.AvailableAt is null || e.AvailableAt <= now).Sum(e => e.Amount);
        var debits = entries.Where(e => e.FromPartyType == party.Type && e.FromPartyId == party.Id).Sum(e => e.Amount);
        var next = eligible.Where(e => e.AvailableAt > now).Select(e => e.AvailableAt).Min();
        return new LedgerBalance(balance, Math.Max(0m, mature - debits - held), held, next);
    }

    private static decimal Net(IEnumerable<LedgerEntry> entries, LedgerParty party) =>
        entries.Sum(e => (e.ToPartyType == party.Type && e.ToPartyId == party.Id ? e.Amount : 0m)
            - (e.FromPartyType == party.Type && e.FromPartyId == party.Id ? e.Amount : 0m));
}
