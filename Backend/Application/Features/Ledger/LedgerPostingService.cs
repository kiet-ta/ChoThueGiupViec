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

    /// <summary>
    /// The sweep side of the refunds, for an order whose latest refund is quiet: under the ledger lock, books the part of the cumulative
    /// refunded total that the ledger lacks. Returns the difference (target minus booked): positive was written, negative is only
    /// reported (the ledger holds more than the source), 0 means they agree.
    /// </summary>
    Task<decimal> ReconcileRefundAsync(LedgerRefundedOrder order, CancellationToken cancellationToken = default);
}

public class LedgerPostingService(ILedgerWriter writer, ILedgerSourceQuery source) : ILedgerPostingService
{
    private static readonly IReadOnlyList<NewLedgerEntry> None = [];

    public Task PostOrderPaidAsync(OrderPaid paid, CancellationToken cancellationToken = default) =>
        writer.AppendAsync([LedgerRules.CustomerPayment(paid.CustomerId, paid.Amount, paid.OrderId, null)], cancellationToken);

    public async Task PostExtensionPaidAsync(ExtensionPaid paid, CancellationToken cancellationToken = default) =>
        await writer.AppendAsync(
            await LedgerSourceEntries.ExtensionPaidAsync(source, paid.OrderId, ToExtensionId(paid.ExtensionId), paid.WorkerId, paid.ExtraAmount, cancellationToken),
            cancellationToken);

    public async Task PostJobCompletedAsync(JobCompleted completed, CancellationToken cancellationToken = default) =>
        await writer.AppendAsync(
            await LedgerSourceEntries.JobCompletedAsync(source, completed.OrderId, completed.AssignmentId, completed.CompletedAtUtc, cancellationToken),
            cancellationToken);

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

        // Everything is decided under the ledger lock (rule 1 of the handoff: the event is the primary record of a refund).
        return writer.AppendAsync(async reader =>
        {
            var existing = await reader.FindExistingKeysAsync([key], cancellationToken);
            if (existing.Contains(key)) return None;

            // The sweep may have booked this refund already (it recovered it from the cumulative total while the event was delayed):
            // book only what the source still shows above the ledger, so the refund is never recorded twice.
            var payments = await source.GetOrderPaymentsAsync(refunded.OrderId, cancellationToken);
            var amount = refunded.Amount;
            if (payments.Count > 0)
            {
                var target = payments.Sum(p => p.RefundedAmount);
                var booked = await reader.GetOrderRefundTotalAsync(refunded.OrderId, cancellationToken);
                amount = Math.Min(amount, Math.Max(0m, target - booked));
                if (amount <= 0) return None;
            }

            return await BuildRefundAsync(reader, refunded.OrderId, refunded.CustomerId, amount, key, payments, cancellationToken);
        }, cancellationToken);
    }

    public async Task<decimal> ReconcileRefundAsync(LedgerRefundedOrder order, CancellationToken cancellationToken = default)
    {
        var difference = 0m;
        await writer.AppendAsync(async reader =>
        {
            var booked = await reader.GetOrderRefundTotalAsync(order.OrderId, cancellationToken);
            difference = order.RefundedTotal - booked;
            if (difference <= 0) return None;
            var payments = await source.GetOrderPaymentsAsync(order.OrderId, cancellationToken);
            return await BuildRefundAsync(
                reader, order.OrderId, order.CustomerId, difference, LedgerKeys.RefundTarget(order.OrderId, order.RefundedTotal), payments, cancellationToken);
        }, cancellationToken);
        return difference;
    }

    /// <summary>
    /// The refund entries plus the payments of the order that are not in the ledger yet. A payment refunded without a payment event (a
    /// late payment that arrives after the order stopped waiting) is booked now, so the refund has money to take from instead of
    /// draining the platform revenue.
    /// </summary>
    private static async Task<IReadOnlyList<NewLedgerEntry>> BuildRefundAsync(
        ILedgerLockedReader reader, long orderId, int customerId, decimal amount, string key, IReadOnlyList<LedgerPaymentSource> payments,
        CancellationToken cancellationToken)
    {
        var paid = payments
            .Select(p => LedgerRules.CustomerPayment(customerId, p.Amount, p.OrderId, p.ExtensionId))
            .DistinctBy(e => e.IdempotencyKey)
            .ToList();
        var have = await reader.FindExistingKeysAsync(paid.Select(e => e.IdempotencyKey).ToArray(), cancellationToken);
        var missing = paid.Where(e => !have.Contains(e.IdempotencyKey)).ToList();

        var clearing = await reader.GetOrderClearingBalanceAsync(orderId, cancellationToken) + missing.Sum(e => e.Amount);
        return missing.Concat(LedgerRules.RefundEntries(orderId, customerId, amount, clearing, key)).ToList();
    }

    public async Task PostPenaltyAsync(DisputeResolved resolved, CancellationToken cancellationToken = default)
    {
        if (resolved.FaultParty is null) return;
        var dispute = await source.GetDisputeAsync(resolved.DisputeId, cancellationToken)
            ?? throw new InvalidOperationException($"Dispute {resolved.DisputeId} was not found.");
        var mine = (await LedgerSourceEntries.PenaltiesAsync(source, dispute, cancellationToken))
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
