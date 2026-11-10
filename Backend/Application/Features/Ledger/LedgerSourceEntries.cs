using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Ledger;

/// <summary>
/// Builds the entries a source fact implies by reading what the event payload lacks. The event handlers and the reconciliation sweep
/// share these functions, so both produce the same entries with the same idempotency keys. Nothing is written here.
/// </summary>
public static class LedgerSourceEntries
{
    /// <summary>JOB_EARNING + COMMISSION of the assignment and the earnings of the paid extensions of the same worker.</summary>
    public static async Task<IReadOnlyList<NewLedgerEntry>> JobCompletedAsync(
        ILedgerSourceQuery source, long orderId, long assignmentId, DateTime completedAtUtc, CancellationToken cancellationToken = default)
    {
        var order = await source.GetOrderAsync(orderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {orderId} of the completed assignment {assignmentId} was not found.");
        var assignment = (await source.GetOrderAssignmentsAsync(orderId, cancellationToken)).FirstOrDefault(a => a.AssignmentId == assignmentId)
            ?? throw new InvalidOperationException($"Assignment {assignmentId} was not found.");

        var entries = new List<NewLedgerEntry>(LedgerRules.JobEntries(order, assignment, completedAtUtc));

        // The extension earnings go only to the worker of the extension, and only when the customer paid it and it still stands.
        foreach (var extension in await source.GetOrderExtensionsAsync(orderId, cancellationToken))
        {
            if (extension.IsPaid && extension.WorkerId == assignment.WorkerId)
            {
                entries.AddRange(LedgerRules.ExtensionEntries(extension, assignment, completedAtUtc));
            }
        }

        return entries;
    }

    /// <summary>
    /// EXTENSION_PAID, plus the shares when the extension worker already finished the job (rare: the customer paid late, so the job
    /// event has come and gone).
    /// </summary>
    public static async Task<IReadOnlyList<NewLedgerEntry>> ExtensionPaidAsync(
        ILedgerSourceQuery source, long orderId, int extensionId, int workerId, decimal amount, CancellationToken cancellationToken = default)
    {
        var order = await source.GetOrderAsync(orderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {orderId} of the paid extension {extensionId} was not found.");
        var entries = new List<NewLedgerEntry> { LedgerRules.CustomerPayment(order.CustomerId, amount, orderId, extensionId) };

        var finished = (await source.GetOrderAssignmentsAsync(orderId, cancellationToken))
            .FirstOrDefault(a => a.WorkerId == workerId && a.IsCompleted && a.CompletedAtUtc is not null);
        if (finished is not null)
        {
            var extension = new LedgerExtensionSource(extensionId, orderId, workerId, amount, true);
            entries.AddRange(LedgerRules.ExtensionEntries(extension, finished, finished.CompletedAtUtc!.Value));
        }

        return entries;
    }

    /// <summary>The entry of a paid payment found by the sweep: ORDER_PAID, EXTENSION_PAID or SUBSCRIPTION (null when there is none).</summary>
    public static async Task<IReadOnlyList<NewLedgerEntry>> PaymentAsync(
        ILedgerSourceQuery source, LedgerSweepPayment payment, CancellationToken cancellationToken = default)
    {
        switch (payment.Purpose)
        {
            case PaymentPurpose.Subscription:
                return payment.AgencyId is { } agencyId && LedgerRules.Subscription(agencyId, payment.Amount, payment.PaymentId) is { } subscription
                    ? [subscription]
                    : [];
            case PaymentPurpose.Order when payment.OrderId is { } orderId:
                {
                    var order = await source.GetOrderAsync(orderId, cancellationToken)
                        ?? throw new InvalidOperationException($"Order {orderId} of payment {payment.PaymentId} was not found.");
                    return [LedgerRules.CustomerPayment(order.CustomerId, payment.Amount, orderId, null)];
                }

            case PaymentPurpose.Extension when payment.ExtensionId is { } extensionId:
                {
                    var extension = await source.GetExtensionAsync(extensionId, cancellationToken)
                        ?? throw new InvalidOperationException($"Extension {extensionId} of payment {payment.PaymentId} was not found.");
                    var order = await source.GetOrderAsync(extension.OrderId, cancellationToken)
                        ?? throw new InvalidOperationException($"Order {extension.OrderId} of payment {payment.PaymentId} was not found.");
                    return [LedgerRules.CustomerPayment(order.CustomerId, payment.Amount, extension.OrderId, extensionId)];
                }

            default:
                return [];
        }
    }

    /// <summary>The penalties of a resolved dispute, one per blamed assignment.</summary>
    public static async Task<IReadOnlyList<(long AssignmentId, NewLedgerEntry Entry)>> PenaltiesAsync(
        ILedgerSourceQuery source, LedgerDisputeSource dispute, CancellationToken cancellationToken = default)
    {
        if (!dispute.IsResolved || dispute.CompensationAmount <= 0) return [];
        return LedgerRules.PenaltyEntries(dispute, await source.GetOrderAssignmentsAsync(dispute.OrderId, cancellationToken));
    }
}
