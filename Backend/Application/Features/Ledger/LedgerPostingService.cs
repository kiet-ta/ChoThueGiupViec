using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Events;

namespace CommonService.Application.Features.Ledger;

/// <summary>
/// Turns the money events into ledger entries (decision D28). Every method is idempotent: the entries carry the keys of
/// <see cref="LedgerKeys"/>, so replaying an event, or the reconciliation sweep running later, writes nothing twice. The methods
/// throw when a source fact is missing; the event handlers catch that and leave the gap to the sweep.
/// </summary>
public interface ILedgerPostingService
{
    Task PostOrderPaidAsync(OrderPaid paid, CancellationToken cancellationToken = default);
    Task PostExtensionPaidAsync(ExtensionPaid paid, CancellationToken cancellationToken = default);
    Task PostJobCompletedAsync(JobCompleted completed, CancellationToken cancellationToken = default);
    Task PostAbsenceFeeAsync(CustomerAbsentApproved approved, CancellationToken cancellationToken = default);
    Task PostRefundAsync(OrderRefunded refunded, CancellationToken cancellationToken = default);
    Task PostPenaltyAsync(DisputeResolved resolved, CancellationToken cancellationToken = default);
}

public class LedgerPostingService(ILedgerWriter writer, ILedgerSourceQuery source) : ILedgerPostingService
{
    private static readonly IReadOnlyList<NewLedgerEntry> None = [];

    public Task PostOrderPaidAsync(OrderPaid paid, CancellationToken cancellationToken = default) =>
        writer.AppendAsync([LedgerRules.CustomerPayment(paid.CustomerId, paid.Amount, paid.OrderId, null)], cancellationToken);

    public async Task PostExtensionPaidAsync(ExtensionPaid paid, CancellationToken cancellationToken = default)
    {
        var extensionId = ToExtensionId(paid.ExtensionId);
        var order = await source.GetOrderAsync(paid.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {paid.OrderId} of the paid extension {extensionId} was not found.");
        var entries = new List<NewLedgerEntry> { LedgerRules.CustomerPayment(order.CustomerId, paid.ExtraAmount, paid.OrderId, extensionId) };

        // Rare: the customer paid after the worker finished the job. Then the shares are due now; the job event has come and gone.
        var finished = (await source.GetOrderAssignmentsAsync(paid.OrderId, cancellationToken))
            .FirstOrDefault(a => a.WorkerId == paid.WorkerId && a.IsCompleted && a.CompletedAtUtc is not null);
        if (finished is not null)
        {
            var extension = new LedgerExtensionSource(extensionId, paid.OrderId, paid.WorkerId, paid.ExtraAmount, true);
            entries.AddRange(LedgerRules.ExtensionEntries(extension, finished, finished.CompletedAtUtc!.Value));
        }

        await writer.AppendAsync(entries, cancellationToken);
    }

    public async Task PostJobCompletedAsync(JobCompleted completed, CancellationToken cancellationToken = default)
    {
        var order = await source.GetOrderAsync(completed.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {completed.OrderId} of the completed assignment {completed.AssignmentId} was not found.");
        var assignment = (await source.GetOrderAssignmentsAsync(completed.OrderId, cancellationToken))
            .FirstOrDefault(a => a.AssignmentId == completed.AssignmentId)
            ?? throw new InvalidOperationException($"Assignment {completed.AssignmentId} was not found.");

        var entries = new List<NewLedgerEntry>(LedgerRules.JobEntries(order, assignment, completed.CompletedAtUtc));

        // The extension earnings go only to the worker of the extension, and only when the customer paid it and it still stands.
        foreach (var extension in await source.GetOrderExtensionsAsync(completed.OrderId, cancellationToken))
        {
            if (extension.IsPaid && extension.WorkerId == assignment.WorkerId)
            {
                entries.AddRange(LedgerRules.ExtensionEntries(extension, assignment, completed.CompletedAtUtc));
            }
        }

        await writer.AppendAsync(entries, cancellationToken);
    }

    public async Task PostAbsenceFeeAsync(CustomerAbsentApproved approved, CancellationToken cancellationToken = default)
    {
        var assignment = (await source.GetOrderAssignmentsAsync(approved.OrderId, cancellationToken))
            .FirstOrDefault(a => a.AssignmentId == approved.AssignmentId)
            ?? throw new InvalidOperationException($"Assignment {approved.AssignmentId} of the approved absence was not found.");
        if (LedgerRules.AbsenceFee(assignment, approved.CompensationAmount) is { } fee)
        {
            await writer.AppendAsync([fee], cancellationToken);
        }
    }

    public Task PostRefundAsync(OrderRefunded refunded, CancellationToken cancellationToken = default)
    {
        if (refunded.Amount <= 0) return Task.CompletedTask;
        var key = LedgerKeys.Refund(refunded.OrderId, refunded.RefundedAtUtc);

        // Everything is decided under the ledger lock: what is booked already, which payments are still missing, and the order's
        // clearing balance (rule 1 of the handoff: the event is the primary record of a refund).
        return writer.AppendAsync(async reader =>
        {
            var booked = await reader.FindExistingKeysAsync([key], cancellationToken);
            if (booked.Contains(key)) return None;

            // A payment refunded without a payment event (a late payment that arrives after the order stopped waiting) is booked now,
            // so the refund has money to take from instead of draining the platform revenue.
            var payments = (await source.GetOrderPaymentsAsync(refunded.OrderId, cancellationToken))
                .Select(p => LedgerRules.CustomerPayment(refunded.CustomerId, p.Amount, p.OrderId, p.ExtensionId))
                .DistinctBy(e => e.IdempotencyKey)
                .ToList();
            var have = await reader.FindExistingKeysAsync(payments.Select(e => e.IdempotencyKey).ToArray(), cancellationToken);
            var missing = payments.Where(e => !have.Contains(e.IdempotencyKey)).ToList();

            var clearing = await reader.GetOrderClearingBalanceAsync(refunded.OrderId, cancellationToken) + missing.Sum(e => e.Amount);
            return missing.Concat(LedgerRules.RefundEntries(refunded.OrderId, refunded.CustomerId, refunded.Amount, clearing, key)).ToList();
        }, cancellationToken);
    }

    public async Task PostPenaltyAsync(DisputeResolved resolved, CancellationToken cancellationToken = default)
    {
        if (resolved.FaultParty is null) return;
        var dispute = await source.GetDisputeAsync(resolved.DisputeId, cancellationToken)
            ?? throw new InvalidOperationException($"Dispute {resolved.DisputeId} was not found.");
        var assignments = await source.GetOrderAssignmentsAsync(dispute.OrderId, cancellationToken);
        var mine = LedgerRules.PenaltyEntries(dispute, assignments)
            .Where(p => p.AssignmentId == resolved.AssignmentId)
            .Select(p => p.Entry)
            .ToList();
        if (mine.Count > 0) await writer.AppendAsync(mine, cancellationToken);
    }

    /// <summary>The event carries the key as long, the entity as int (like the ExtensionDeclined handler).</summary>
    private static int ToExtensionId(long extensionId) =>
        extensionId is < 1 or > int.MaxValue
            ? throw new InvalidOperationException($"The extension id {extensionId} is not valid.")
            : (int)extensionId;
}
