using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Payments;

/// <summary>
/// Settles a gateway payment whose purpose is not an order or an extension (those are Payments' own, BE-M2-05/08). The IPN endpoint finds
/// the handler by <see cref="Purpose"/>: Agencies (M5) implements one for <c>SUBSCRIPTION</c> and one for the escrow deposit.
/// </summary>
public interface IPaymentPurposeSettlement
{
    PaymentPurpose Purpose { get; }

    /// <summary>
    /// The gateway reported SUCCESS for <paramref name="transaction"/>. Marks it paid and applies the purchase in one transaction; true
    /// when this call did it, false for a repeat or a lost race (idempotent). <paramref name="wasExpired"/> is true when we had already
    /// marked the payment EXPIRED (decisions Q24 / P4): the money was received, so the handler still applies or refunds it.
    /// </summary>
    Task<bool> SettleSuccessAsync(PaymentTransaction transaction, bool wasExpired, string rawBody, CancellationToken cancellationToken = default);
}
