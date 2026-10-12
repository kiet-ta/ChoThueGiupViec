using Backend.Application.Features.Agencies.Dtos;
using Backend.Domain.Entities;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Backend.Application.Features.Agencies;

/// <summary>
/// Repository for agency subscription operations
/// </summary>
public interface ISubscriptionRepository
{
    /// <summary>
    /// Gets a subscription package by its ID
    /// </summary>
    Task<SubscriptionPackage?> GetPackageByIdAsync(int packageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active subscription packages
    /// </summary>
    Task<IReadOnlyList<SubscriptionPackage>> GetActivePackagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the active subscription for an agency
    /// </summary>
    Task<PartnerSubscription?> GetActiveSubscriptionByAgencyIdAsync(int agencyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new subscription
    /// </summary>
    Task AddAsync(PartnerSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing subscription
    /// </summary>
    Task UpdateAsync(PartnerSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves all changes made in this repository
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}