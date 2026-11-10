using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Infrastructure.Fakes;

public class FakeLedgerQuery : ILedgerQuery
{
    public List<LedgerEntry> Entries { get; } = [];
    public Dictionary<int, decimal> Held { get; } = [];

    public Task<IReadOnlyList<LedgerEntry>> GetPartyEntriesAsync(LedgerPartyType type, int? id, DateTime? beforeUtc = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerEntry>>(Entries.Where(e =>
            ((e.ToPartyType == type && e.ToPartyId == id) || (e.FromPartyType == type && e.FromPartyId == id)) &&
            (!beforeUtc.HasValue || e.CreatedAt < beforeUtc.Value)).OrderBy(e => e.EntryId).ToArray());

    public Task<decimal> GetHeldAsync(int workerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Held.GetValueOrDefault(workerId));

    public Task<IReadOnlyList<(LedgerPartyType Type, int Id)>> GetSettlementPartiesAsync(DateTime beforeUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<(LedgerPartyType, int)>>(Entries
            .Where(e => e.CreatedAt < beforeUtc)
            .SelectMany(e => new[] { (e.ToPartyType, e.ToPartyId), (e.FromPartyType, e.FromPartyId) })
            .Where(p => p.Item1 is LedgerPartyType.Worker or LedgerPartyType.Agency && p.Item2 is not null)
            .Select(p => (p.Item1, p.Item2!.Value))
            .Distinct()
            .OrderBy(p => p.Item1).ThenBy(p => p.Item2)
            .ToArray());

    public Task<IReadOnlyList<LedgerEntry>> GetCommissionsAsync(LedgerRefType refType, IReadOnlyCollection<long> refIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerEntry>>(Entries.Where(e => e.EntryType == LedgerEntryType.Commission && e.RefType == refType && refIds.Contains(e.RefId)).ToArray());

    public Task<DateTime?> GetFirstEntryAtAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Entries.Count == 0 ? (DateTime?)null : Entries.Min(e => e.CreatedAt));
}
