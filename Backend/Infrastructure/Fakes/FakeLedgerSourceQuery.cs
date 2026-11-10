using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

public class FakeLedgerSourceQuery : ILedgerSourceQuery
{
    public List<LedgerOrderSource> Orders { get; } = [];
    public List<LedgerAssignmentSource> Assignments { get; } = [];
    public List<LedgerExtensionSource> Extensions { get; } = [];
    public List<LedgerDisputeSource> Disputes { get; } = [];
    public List<LedgerPaymentSource> Payments { get; } = [];
    public List<LedgerSweepPayment> SweepPayments { get; } = [];
    public List<LedgerAbsenceSource> Absences { get; } = [];
    public List<LedgerEscrowSource> EscrowTransactions { get; } = [];
    public List<LedgerRefundedOrder> RefundedOrders { get; } = [];

    public Task<LedgerOrderSource?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Orders.FirstOrDefault(o => o.OrderId == orderId));

    public Task<IReadOnlyList<LedgerAssignmentSource>> GetOrderAssignmentsAsync(long orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerAssignmentSource>>(Assignments.Where(a => a.OrderId == orderId).OrderBy(a => a.AssignmentId).ToArray());

    public Task<LedgerExtensionSource?> GetExtensionAsync(int extensionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Extensions.FirstOrDefault(e => e.ExtensionId == extensionId));

    public Task<IReadOnlyList<LedgerExtensionSource>> GetOrderExtensionsAsync(long orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerExtensionSource>>(Extensions.Where(e => e.OrderId == orderId).OrderBy(e => e.ExtensionId).ToArray());

    public Task<IReadOnlyList<LedgerPaymentSource>> GetOrderPaymentsAsync(long orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerPaymentSource>>(Payments.Where(p => p.OrderId == orderId).OrderBy(p => p.PaymentId).ToArray());

    public Task<IReadOnlyList<LedgerSweepPayment>> GetPaidPaymentsSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerSweepPayment>>(SweepPayments.Where(p => p.AtUtc >= sinceUtc).OrderBy(p => p.PaymentId).ToArray());

    public Task<IReadOnlyList<LedgerAssignmentSource>> GetCompletedAssignmentsSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerAssignmentSource>>(
            Assignments.Where(a => a.IsCompleted && a.CompletedAtUtc >= sinceUtc).OrderBy(a => a.AssignmentId).ToArray());

    public Task<IReadOnlyList<LedgerAbsenceSource>> GetAbsenceFeesSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerAbsenceSource>>(Absences.Where(a => a.Assignment.CompletedAtUtc is null || a.Assignment.CompletedAtUtc >= sinceUtc).ToArray());

    public Task<IReadOnlyList<LedgerDisputeSource>> GetResolvedDisputesSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerDisputeSource>>(
            Disputes.Where(d => d.IsResolved && (d.ResolvedAtUtc is null || d.ResolvedAtUtc >= sinceUtc)).OrderBy(d => d.DisputeId).ToArray());

    public Task<IReadOnlyList<LedgerEscrowSource>> GetEscrowTransactionsSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerEscrowSource>>(EscrowTransactions.Where(e => e.CreatedAtUtc >= sinceUtc).OrderBy(e => e.EscrowTxnId).ToArray());

    public Task<IReadOnlyList<LedgerRefundedOrder>> GetRefundedOrdersAsync(DateTime sinceUtc, DateTime quietBeforeUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerRefundedOrder>>(
            RefundedOrders.Where(o => o.LatestRefundedAtUtc >= sinceUtc && o.LatestRefundedAtUtc <= quietBeforeUtc).OrderBy(o => o.OrderId).ToArray());

    public Task<LedgerDisputeSource?> GetDisputeAsync(int disputeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Disputes.FirstOrDefault(d => d.DisputeId == disputeId));
}
