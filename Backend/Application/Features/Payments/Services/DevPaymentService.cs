using CommonService.Application.Exceptions;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Payments.Services;

/// <summary>An open sandbox payment as the development tool lists it.</summary>
public sealed record DevPaymentDto(
    long PaymentId, string Purpose, long? OrderId, int? SubscriptionId, int? AgencyId, decimal Amount, string GatewayTxnRef, DateTime CreatedAt);

/// <summary>Read side of the development tool (UAT-PAY).</summary>
public interface IDevPaymentQuery
{
    /// <summary>The payments still <c>PENDING</c>, newest first.</summary>
    Task<IReadOnlyList<DevPaymentDto>> ListPendingAsync(int take, CancellationToken cancellationToken = default);
}

public static class DevPaymentErrorCodes
{
    public const string NotFakeGateway = "DEV_CONFIRM_FAKE_ONLY";
}

/// <summary>
/// Development tool (UAT-PAY, decisions Q04 follow-up): finishes a payment of the Fake gateway the way its IPN would, so a tester can
/// pay without calling the callback by hand. It goes through <see cref="IIpnService"/>, hence through the real settlement of the purpose
/// (order dispatch, escrow credit, subscription activation). The host decides where it is reachable (Development only).
/// </summary>
public sealed class DevPaymentService(IDevPaymentQuery query, IPaymentRepository payments, IIpnService ipn)
{
    /// <summary>The reference prefix of the Fake gateway (<c>FakePaymentGateway</c>); a MoMo sandbox reference never has it.</summary>
    public const string FakeReferencePrefix = "FAKE-";

    public const int MaxListed = 100;

    public Task<IReadOnlyList<DevPaymentDto>> ListAsync(CancellationToken cancellationToken = default) =>
        query.ListPendingAsync(MaxListed, cancellationToken);

    public async Task<IpnOutcome> ConfirmAsync(long paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await payments.GetPaymentAsync(paymentId, cancellationToken)
            ?? throw new NotFoundException("Payment", paymentId);
        if (!payment.GatewayTxnRef.StartsWith(FakeReferencePrefix, StringComparison.Ordinal))
        {
            throw new BusinessRuleViolationException(
                "Only a payment of the Fake gateway can be confirmed here; a MoMo sandbox payment is paid on MoMo.", DevPaymentErrorCodes.NotFakeGateway);
        }

        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["gatewayTxnRef"] = payment.GatewayTxnRef,
            ["amount"] = payment.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["status"] = "success",
            ["signature"] = "valid",
        };
        var outcome = await ipn.HandleAsync(payload, System.Text.Json.JsonSerializer.Serialize(payload), cancellationToken);
        if (outcome == IpnOutcome.Rejected)
        {
            throw new BusinessRuleViolationException("The payment was rejected (amount or purpose mismatch).", "DEV_CONFIRM_REJECTED");
        }

        return outcome;
    }
}
