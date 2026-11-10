using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Interfaces.Ports;

/// <summary>Read-only access to a party's ledger entries and held withdrawal amount.</summary>
public interface ILedgerQuery
{
    Task<IReadOnlyList<LedgerEntry>> GetPartyEntriesAsync(LedgerPartyType type, int? id, DateTime? beforeUtc = null, CancellationToken cancellationToken = default);
    Task<decimal> GetHeldAsync(int workerId, CancellationToken cancellationToken = default);

    /// <summary>When the first ledger entry was written: the start of the ledger (decision D), or null while it is empty.</summary>
    Task<DateTime?> GetFirstEntryAtAsync(CancellationToken cancellationToken = default);
}
