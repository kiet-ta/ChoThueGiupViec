using CommonService.Application.Common.Options;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Agencies;

/// <summary>Persistence of the Verified Partner label (<c>PARTNER_AGENCY.is_verified_partner</c>).</summary>
public interface IVerifiedPartnerRepository
{
    /// <summary>
    /// Sets the label to true for the agencies (all, or just <paramref name="agencyId"/>) that have an <c>ACTIVE</c> PRO subscription that
    /// started on or before <paramref name="today"/> and whose <c>end_date</c> plus <paramref name="graceDays"/> has not passed; to false for
    /// the others. Returns how many agencies changed.
    /// </summary>
    Task<int> SyncAsync(DateOnly today, int graceDays, int? agencyId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The Verified Partner label is a paid benefit of the Pro package (leader decision 2026-10-14, decisions D17): it follows the package in
/// force, grace days included (Q08). Set when a subscription is activated and kept right by the hourly expiry sweep.
/// </summary>
public sealed class VerifiedPartnerSync(
    IVerifiedPartnerRepository repository,
    IClock clock,
    IOptions<BusinessRules> rules,
    ILogger<VerifiedPartnerSync>? logger = null)
{
    private readonly ILogger<VerifiedPartnerSync> _logger = logger ?? NullLogger<VerifiedPartnerSync>.Instance;

    public async Task<int> SyncAsync(int? agencyId = null, CancellationToken cancellationToken = default)
    {
        var changed = await repository.SyncAsync(clock.LocalToday, rules.Value.Subscription.GraceDays, agencyId, cancellationToken);
        if (changed > 0)
        {
            _logger.LogInformation("Verified Partner label changed for {Count} agenc(ies).", changed);
        }

        return changed;
    }
}

/// <summary>A new Pro (or any) subscription may switch the label on: recompute it for that agency right after the activation.</summary>
public sealed class SubscriptionActivatedVerifiedPartnerHandler(VerifiedPartnerSync sync) : INotificationHandler<SubscriptionActivated>
{
    public async Task Handle(SubscriptionActivated notification, CancellationToken cancellationToken) =>
        await sync.SyncAsync(notification.AgencyId, cancellationToken);
}
