using CommonService.Domain.Enums;

namespace CommonService.Application.Interfaces.Ports;

/// <summary>An order as the ledger needs it: who paid, the total split over the seats.</summary>
public record LedgerOrderSource(long OrderId, int CustomerId, decimal TotalAmount, int RequiredWorkers);

/// <summary>One assignment (seat <see cref="Seat"/> of an order). <see cref="AgencyId"/> set means the agency, not the worker, is paid.</summary>
public record LedgerAssignmentSource(
    long AssignmentId, long OrderId, int WorkerId, int? AgencyId, int Seat, decimal GrossAmount, decimal CommissionRate, DateTime? CompletedAtUtc,
    bool IsCompleted);

/// <summary>An extension ("Làm lần 2"); <see cref="IsPaid"/> is true while it is PAID or ACCEPTED (not declined, expired or unpaid).</summary>
public record LedgerExtensionSource(int ExtensionId, long OrderId, int WorkerId, decimal ExtraAmount, bool IsPaid);

/// <summary>A dispute ticket; <see cref="IsResolved"/> is false for open, in-review and dismissed tickets.</summary>
public record LedgerDisputeSource(
    int DisputeId, long OrderId, bool IsResolved, FaultParty? FaultParty, string Category, decimal CompensationAmount);

/// <summary>A payment that reached SUCCESS (or REFUNDED later) for an order's ORDER payment or one of its extensions.</summary>
public record LedgerPaymentSource(long PaymentId, long OrderId, int? ExtensionId, decimal Amount);

/// <summary>
/// Read-only access (no tracking) to the source facts the ledger cannot take from an event payload (decision D28). The
/// implementation lives in the Ledger module and only reads; nothing here changes another module's data.
/// </summary>
public interface ILedgerSourceQuery
{
    Task<LedgerOrderSource?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>Every assignment of the order, whatever its status, ordered by assignment id.</summary>
    Task<IReadOnlyList<LedgerAssignmentSource>> GetOrderAssignmentsAsync(long orderId, CancellationToken cancellationToken = default);

    Task<LedgerExtensionSource?> GetExtensionAsync(int extensionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LedgerExtensionSource>> GetOrderExtensionsAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>The paid ORDER payment and the paid EXTENSION payments of the order (status SUCCESS or REFUNDED).</summary>
    Task<IReadOnlyList<LedgerPaymentSource>> GetOrderPaymentsAsync(long orderId, CancellationToken cancellationToken = default);

    Task<LedgerDisputeSource?> GetDisputeAsync(int disputeId, CancellationToken cancellationToken = default);
}
