using System;

namespace CommonService.Application.Features.Agencies.Dtos;

/// <summary>
/// Data transfer object for subscription package
/// </summary>
public sealed record PackageDto(
    int Id,
    string Name,
    string Description,
    int WorkerLimit,
    int CommissionRate,
    long? MonthlyPriceVnd,
    long? QuarterlyPriceVnd,
    DateTime CreatedAt
);