using System;

namespace CommonService.Application.Features.Agencies.Dtos;

/// <summary>
/// Data transfer object for agency subscription
/// </summary>
public sealed record SubscriptionDto(
    int Id,
    int AgencyId,
    int PackageId,
    PackageDto Package,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    bool AutoRenew,
    DateTime CreatedAt,
    DateTime UpdatedAt
);