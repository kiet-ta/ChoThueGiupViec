using CommonService.Application.Features.Payments.Services;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Tests.Payments;

/// <summary>Stands in for the refund core where a test only needs to know WHAT was asked to be refunded (late payments, P4).</summary>
internal sealed class RecordingLateRefunds : IRefundService, IExtensionRefundService
{
    private readonly object _gate = new();
    private readonly List<RefundRequest> _orders = [];
    private readonly List<(int ExtensionId, string Reason)> _extensions = [];

    public bool Succeed { get; set; } = true;

    public IReadOnlyList<RefundRequest> OrderRefunds { get { lock (_gate) { return _orders.ToArray(); } } }
    public IReadOnlyList<(int ExtensionId, string Reason)> ExtensionRefunds { get { lock (_gate) { return _extensions.ToArray(); } } }

    public Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate) { _orders.Add(request); }
        return Task.FromResult(Succeed ? new RefundResult(true, request.Amount, null) : new RefundResult(false, 0m, "gateway down"));
    }

    public Task<RefundResult> RefundExtensionAsync(int extensionId, string reason, CancellationToken cancellationToken = default)
    {
        lock (_gate) { _extensions.Add((extensionId, reason)); }
        return Task.FromResult(Succeed ? new RefundResult(true, 0m, null) : new RefundResult(false, 0m, "gateway down"));
    }
}
