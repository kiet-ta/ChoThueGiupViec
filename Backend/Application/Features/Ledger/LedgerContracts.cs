using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Ledger;

public record LedgerParty(LedgerPartyType Type, int? Id);

public record NewLedgerEntry(
    LedgerParty From, LedgerParty To, decimal Amount, LedgerEntryType Type,
    LedgerRefType RefType, long RefId, long? OrderId, DateTime? AvailableAtUtc,
    string IdempotencyKey, string? Note = null, int? CreatedByAdminId = null);

public interface ILedgerLockedReader
{
    Task<decimal> GetOrderRefundTotalAsync(long orderId, CancellationToken cancellationToken = default);
    Task<decimal> GetOrderClearingBalanceAsync(long orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<string>> FindExistingKeysAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default);
}

public interface ILedgerRepository : ILedgerLockedReader
{
    Task<T> ExecuteLockedAsync<T>(Func<ILedgerLockedReader, Task<T>> action, CancellationToken cancellationToken = default);
    Task<string?> GetLastHashAsync(CancellationToken cancellationToken = default);
    Task AppendAndSealAsync(LedgerEntry entry, Func<LedgerEntry, string> hash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LedgerEntry>> ReadChainAsync(CancellationToken cancellationToken = default);
}

public interface ILedgerWriter
{
    Task<IReadOnlyList<LedgerEntry>> AppendAsync(IReadOnlyList<NewLedgerEntry> entries, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LedgerEntry>> AppendAsync(Func<ILedgerLockedReader, Task<IReadOnlyList<NewLedgerEntry>>> build, CancellationToken cancellationToken = default);
}

public record LedgerChainResult(bool Ok, int CheckedCount, string? FirstBrokenEntryCode);

public interface ILedgerChainVerifier
{
    Task<LedgerChainResult> VerifyAsync(CancellationToken cancellationToken = default);
}
