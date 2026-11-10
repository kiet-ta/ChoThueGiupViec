using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

public class FakeLedgerSourceQuery : ILedgerSourceQuery
{
    public List<LedgerOrderSource> Orders { get; } = [];
    public List<LedgerAssignmentSource> Assignments { get; } = [];
    public List<LedgerExtensionSource> Extensions { get; } = [];
    public List<LedgerDisputeSource> Disputes { get; } = [];
    public List<LedgerPaymentSource> Payments { get; } = [];

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

    public Task<LedgerDisputeSource?> GetDisputeAsync(int disputeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Disputes.FirstOrDefault(d => d.DisputeId == disputeId));
}
