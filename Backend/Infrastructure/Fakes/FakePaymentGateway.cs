using System.Collections.Concurrent;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;

namespace CommonService.Infrastructure.Fakes;

/// <summary>
/// In-memory gateway. IPN payload keys of this Fake (the real MoMo adapter reads MoMo's own documentation, decisions G-7):
/// <c>gatewayTxnRef</c>, <c>amount</c>, <c>status</c> (<c>success</c> | <c>pending</c> | <c>expired</c>) and <c>signature</c> (valid only when it is <c>valid</c>).
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, (PaymentStatus Status, decimal Amount)> _transactions = new();

    public IReadOnlyCollection<CreatePaymentRequest> Created => _created.ToArray();

    private readonly ConcurrentQueue<CreatePaymentRequest> _created = new();

    /// <summary>When false, <see cref="RefundAsync"/> reports that the gateway has no refund API.</summary>
    public bool RefundSupported { get; set; } = true;

    public Task<PaymentQr> CreateQrAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        var reference = $"FAKE-{Guid.NewGuid():N}";
        _transactions[reference] = (PaymentStatus.Pending, request.Amount);
        _created.Enqueue(request);
        return Task.FromResult(new PaymentQr(reference, $"fake-qr://{reference}", $"https://sandbox.invalid/pay/{reference}", request.ExpiresAtUtc));
    }

    public Task<IpnVerification> VerifyIpnAsync(IReadOnlyDictionary<string, string> payload, CancellationToken cancellationToken = default)
    {
        if (!payload.TryGetValue("signature", out var signature) || signature != "valid")
        {
            return Task.FromResult(new IpnVerification(false, null, 0m, PaymentStatus.Pending));
        }

        payload.TryGetValue("gatewayTxnRef", out var reference);
        var amount = payload.TryGetValue("amount", out var rawAmount) && decimal.TryParse(rawAmount, out var parsed) ? parsed : 0m;
        var status = payload.TryGetValue("status", out var rawStatus) ? rawStatus switch
        {
            "success" => PaymentStatus.Success,
            "expired" => PaymentStatus.Expired,
            _ => PaymentStatus.Pending
        } : PaymentStatus.Pending;
        return Task.FromResult(new IpnVerification(true, reference, amount, status));
    }

    public Task<GatewayTransactionStatus> QueryStatusAsync(string gatewayTxnRef, CancellationToken cancellationToken = default)
    {
        var known = _transactions.TryGetValue(gatewayTxnRef, out var entry);
        return Task.FromResult(new GatewayTransactionStatus(gatewayTxnRef, known ? entry.Status : PaymentStatus.Pending, known ? entry.Amount : 0m));
    }

    public Task<GatewayRefundResult> RefundAsync(string gatewayTxnRef, decimal amount, string reason, CancellationToken cancellationToken = default) =>
        Task.FromResult(RefundSupported
            ? new GatewayRefundResult(true, true, null)
            : new GatewayRefundResult(false, false, "The Fake gateway has no refund API."));

    /// <summary>Test helper: mark a created transaction as paid so that <see cref="QueryStatusAsync"/> reports it.</summary>
    public void MarkPaid(string gatewayTxnRef)
    {
        if (_transactions.TryGetValue(gatewayTxnRef, out var entry))
        {
            _transactions[gatewayTxnRef] = (PaymentStatus.Success, entry.Amount);
        }
    }
}
