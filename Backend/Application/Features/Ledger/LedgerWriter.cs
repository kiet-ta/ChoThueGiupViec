using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.ValueObjects;

namespace CommonService.Application.Features.Ledger;

public class LedgerWriter(ILedgerRepository repository, IClock clock) : ILedgerWriter
{
    public Task<IReadOnlyList<LedgerEntry>> AppendAsync(IReadOnlyList<NewLedgerEntry> entries, CancellationToken cancellationToken = default) =>
        AppendAsync(_ => Task.FromResult(entries), cancellationToken);

    public Task<IReadOnlyList<LedgerEntry>> AppendAsync(Func<ILedgerLockedReader, Task<IReadOnlyList<NewLedgerEntry>>> build, CancellationToken cancellationToken = default) =>
        repository.ExecuteLockedAsync<IReadOnlyList<LedgerEntry>>(async reader =>
        {
            var entries = await build(reader);
            foreach (var entry in entries) Validate(entry);
            var existing = await repository.FindExistingKeysAsync(entries.Select(e => e.IdempotencyKey).ToArray(), cancellationToken);
            var seen = new HashSet<string>(existing, StringComparer.Ordinal);
            var previous = await repository.GetLastHashAsync(cancellationToken) ?? LedgerHash.Genesis;
            var written = new List<LedgerEntry>();
            foreach (var entry in entries)
            {
                if (!seen.Add(entry.IdempotencyKey)) continue;
                var row = new LedgerEntry
                {
                    FromPartyType = entry.From.Type,
                    FromPartyId = entry.From.Id,
                    ToPartyType = entry.To.Type,
                    ToPartyId = entry.To.Id,
                    Amount = entry.Amount,
                    EntryType = entry.Type,
                    RefType = entry.RefType,
                    RefId = entry.RefId,
                    OrderId = entry.OrderId,
                    AvailableAt = entry.AvailableAtUtc,
                    IdempotencyKey = entry.IdempotencyKey,
                    Note = entry.Note,
                    CreatedByAdminId = entry.CreatedByAdminId,
                    PrevHash = previous,
                    EntryHash = LedgerHash.Genesis,
                    CreatedAt = clock.UtcNow,
                };
                await repository.AppendAndSealAsync(row, LedgerHash.Compute, cancellationToken);
                previous = row.EntryHash;
                written.Add(row);
            }
            return written;
        }, cancellationToken);

    private static void Validate(NewLedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.From is null || entry.To is null || entry.From == entry.To)
            throw new ArgumentException("Ledger parties must be distinct.", nameof(entry));
        if (entry.Amount <= 0 || entry.Amount != Vnd.Round(entry.Amount))
            throw new ArgumentException("Ledger amount must be a positive whole number of VND.", nameof(entry));
        if (string.IsNullOrWhiteSpace(entry.IdempotencyKey) || entry.IdempotencyKey.Length > 80)
            throw new ArgumentException("Ledger idempotency key must have 1 to 80 characters.", nameof(entry));
    }
}

public class LedgerChainVerifier(ILedgerRepository repository) : ILedgerChainVerifier
{
    public async Task<LedgerChainResult> VerifyAsync(CancellationToken cancellationToken = default)
    {
        var entries = await repository.ReadChainAsync(cancellationToken);
        var previous = LedgerHash.Genesis;
        var checkedCount = 0;
        foreach (var entry in entries)
        {
            checkedCount++;
            if (entry.PrevHash != previous || entry.EntryHash != LedgerHash.Compute(entry))
                return new LedgerChainResult(false, checkedCount, entry.EntryCode);
            previous = entry.EntryHash;
        }
        return new LedgerChainResult(true, checkedCount, null);
    }
}
