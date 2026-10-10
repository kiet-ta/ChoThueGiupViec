using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CommonService.Application.Features.Booking;

/// <summary>
/// Gives the agency capacity of a cancelled PREMIUM order back (contracts booking.md 3.3 rule 6, payments.md 3.3). Call it AFTER the
/// cancelling transaction committed. The order is already cancelled, so a failing release is logged and never thrown.
/// Known gap: a failed release is not retried (no retry mechanism yet); the slots stay held until someone releases them.
/// </summary>
public static class PremiumHoldRelease
{
    public static async Task ReleaseAsync(IAgencyCapacityService capacity, ServiceTier serviceTier, long orderId, ILogger logger)
    {
        if (serviceTier != ServiceTier.Premium)
        {
            return;
        }

        try
        {
            // Not the caller's token: the cancellation is committed, the hold must not outlive it because a request was aborted.
            await capacity.ReleaseByOrderAsync(orderId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Order {OrderId} was cancelled but its Premium capacity hold could not be released; it must be released by hand.", orderId);
        }
    }
}
