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
}
