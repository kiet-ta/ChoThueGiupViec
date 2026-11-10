using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Ledger;

/// <summary>Ledger settings, bound from the "Ledger" configuration section; every value has a default in code.</summary>
public class LedgerOptions
{
    /// <summary>Sources older than this are never reconciled (decision D: no backfill). Null: the time of the first ledger entry.</summary>
    public DateTime? StartUtc { get; set; }

    /// <summary>A refunded order is reconciled only when its latest refund is at least this old (a refund is reserved before the gateway answers).</summary>
    public int RefundQuietMinutes { get; set; } = 10;

    /// <summary>How often the reconciliation worker runs.</summary>
    public int SweepIntervalMinutes { get; set; } = 5;

    /// <summary>The sweep looks back at most this many days from now, to bound the work of every run.</summary>
    public int LookbackDays { get; set; } = 60;
}

/// <summary>What the ledger lacks for one entry type: <see cref="ExcessAmount"/> is what it holds above the source (refunds only).</summary>
public record LedgerReconciliationItem(string EntryType, int MissingCount, decimal MissingAmount, decimal ExcessAmount);

public record LedgerReconciliationReport(IReadOnlyList<LedgerReconciliationItem> Items);

/// <summary>How many entries were missing and how many the sweep wrote (the rest were booked in parallel by an event).</summary>
public record LedgerSweepResult(int Missing, int Written, int RefundOrders);

public interface ILedgerReconciliationService
{
    /// <summary>Compares every source with the ledger without writing: the missing rows and amounts per entry type.</summary>
    Task<LedgerReconciliationReport> ReportAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the missing entries with the key the event would have used, then the quiet refunds from the cumulative totals.</summary>
    Task<LedgerSweepResult> SweepAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The safety net of the ledger (contract ledger.md section 8). Reads the source tables through <see cref="ILedgerSourceQuery"/> and
/// never changes them; everything it writes goes through <see cref="ILedgerWriter"/> with the same idempotency keys as the events, so
/// an event that arrives late finds its entry already there.
/// </summary>
public class LedgerReconciliationService(
    ILedgerSourceQuery source,
    ILedgerQuery ledgerQuery,
    ILedgerRepository repository,
    ILedgerWriter writer,
    ILedgerPostingService posting,
    IClock clock,
    IOptions<LedgerOptions> options,
    ILogger<LedgerReconciliationService> logger) : ILedgerReconciliationService
{
    private const int KeyChunk = 500;
    private const int WriteBatch = 200;

    public async Task<LedgerReconciliationReport> ReportAsync(CancellationToken cancellationToken = default)
    {
        var since = await SinceAsync(cancellationToken);
        if (since is null) return new LedgerReconciliationReport([]);

        var missing = await MissingAsync(since.Value, cancellationToken);
        var items = missing
            .GroupBy(e => e.Type)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Amount: g.Sum(e => e.Amount), Excess: 0m));

        foreach (var order in await QuietRefundsAsync(since.Value, cancellationToken))
        {
            var difference = order.RefundedTotal - await repository.GetOrderRefundTotalAsync(order.OrderId, cancellationToken);
            if (difference == 0) continue;
            var (count, amount, excess) = items.GetValueOrDefault(LedgerEntryType.Refund);
            items[LedgerEntryType.Refund] = difference > 0 ? (count + 1, amount + difference, excess) : (count, amount, excess - difference);
        }

        return new LedgerReconciliationReport(items
            .OrderBy(i => i.Key)
            .Select(i => new LedgerReconciliationItem(DbEnum.ToDb(i.Key), i.Value.Count, i.Value.Amount, i.Value.Excess))
            .ToList());
    }

    public async Task<LedgerSweepResult> SweepAsync(CancellationToken cancellationToken = default)
    {
        var since = await SinceAsync(cancellationToken);
        if (since is null) return new LedgerSweepResult(0, 0, 0);

        var missing = await MissingAsync(since.Value, cancellationToken);
        var written = 0;
        foreach (var batch in missing.Chunk(WriteBatch))
        {
            written += (await writer.AppendAsync(batch, cancellationToken)).Count;
        }

        var refunds = 0;
        foreach (var order in await QuietRefundsAsync(since.Value, cancellationToken))
        {
            try
            {
                if (await posting.ReconcileRefundAsync(order, cancellationToken) != 0) refunds++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Ledger sweep: could not reconcile the refunds of order {OrderId}.", order.OrderId);
            }
        }

        if (written > 0 || refunds > 0)
        {
            logger.LogWarning("Ledger sweep wrote {Written} of {Missing} missing entries and reconciled the refunds of {Refunds} order(s).",
                written, missing.Count, refunds);
        }

        return new LedgerSweepResult(missing.Count, written, refunds);
    }

    /// <summary>The ledger start (decision D) limited to the look-back window; null while the ledger has no entry and no start is configured.</summary>
    private async Task<DateTime?> SinceAsync(CancellationToken cancellationToken)
    {
        var start = options.Value.StartUtc ?? await ledgerQuery.GetFirstEntryAtAsync(cancellationToken);
        if (start is null) return null;
        var floor = clock.UtcNow.AddDays(-options.Value.LookbackDays);
        return start.Value > floor ? start : floor;
    }

    private async Task<IReadOnlyList<LedgerRefundedOrder>> QuietRefundsAsync(DateTime since, CancellationToken cancellationToken) =>
        await source.GetRefundedOrdersAsync(since, clock.UtcNow.AddMinutes(-options.Value.RefundQuietMinutes), cancellationToken);

    /// <summary>Every entry the sources imply (events' keys) that the ledger does not hold.</summary>
    private async Task<List<NewLedgerEntry>> MissingAsync(DateTime since, CancellationToken cancellationToken)
    {
        var expected = new List<NewLedgerEntry>();

        foreach (var payment in await source.GetPaidPaymentsSinceAsync(since, cancellationToken))
        {
            await TryAddAsync(expected, $"payment {payment.PaymentId}", () => LedgerSourceEntries.PaymentAsync(source, payment, cancellationToken));
        }

        foreach (var assignment in await source.GetCompletedAssignmentsSinceAsync(since, cancellationToken))
        {
            await TryAddAsync(expected, $"assignment {assignment.AssignmentId}",
                () => LedgerSourceEntries.JobCompletedAsync(source, assignment.OrderId, assignment.AssignmentId, assignment.CompletedAtUtc!.Value, cancellationToken));
        }

        foreach (var absence in await source.GetAbsenceFeesSinceAsync(since, cancellationToken))
        {
            if (LedgerRules.AbsenceFee(absence.Assignment, absence.Fee) is { } fee) expected.Add(fee);
        }

        foreach (var dispute in await source.GetResolvedDisputesSinceAsync(since, cancellationToken))
        {
            await TryAddAsync(expected, $"dispute {dispute.DisputeId}",
                async () => (await LedgerSourceEntries.PenaltiesAsync(source, dispute, cancellationToken)).Select(p => p.Entry).ToList());
        }

        foreach (var escrow in await source.GetEscrowTransactionsSinceAsync(since, cancellationToken))
        {
            if (LedgerRules.Escrow(escrow) is { } entry) expected.Add(entry);
        }

        var unique = expected.DistinctBy(e => e.IdempotencyKey).ToList();
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var keys in unique.Select(e => e.IdempotencyKey).Chunk(KeyChunk))
        {
            existing.UnionWith(await repository.FindExistingKeysAsync(keys, cancellationToken));
        }

        return unique.Where(e => !existing.Contains(e.IdempotencyKey)).ToList();
    }

    /// <summary>One broken source (e.g. a payment of a deleted order) must not stop the others: it is logged and skipped.</summary>
    private async Task TryAddAsync(List<NewLedgerEntry> expected, string what, Func<Task<IReadOnlyList<NewLedgerEntry>>> build)
    {
        try
        {
            expected.AddRange(await build());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ledger sweep: skipped {Source}.", what);
        }
    }
}
