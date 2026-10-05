using MediatR;

namespace CommonService.Domain.Events;

/// <summary>Published by Agencies (M5) when a partner subscription is activated.</summary>
public sealed record SubscriptionActivated(
    long SubId,
    int AgencyId,
    int PackageId,
    string PackageCode,
    DateTime StartDateUtc,
    DateTime EndDateUtc) : INotification;
