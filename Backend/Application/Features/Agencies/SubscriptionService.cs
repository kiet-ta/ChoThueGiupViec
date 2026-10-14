using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Payments;
using CommonService.Application.Features.Payments.Services;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Agencies;

/// <summary>The worker quota in force (decisions Q08); what features that add workers (import, roster) ask before they write.</summary>
public interface IWorkerQuotaGuard
{
    /// <summary>Throws a 409 <see cref="SubscriptionErrorCodes.QuotaExceeded"/> when adding the workers would exceed the quota in force.</summary>
    Task EnsureCanAddWorkersAsync(int agencyId, int additionalWorkers, CancellationToken cancellationToken = default);
}

/// <summary>Persistence of subscription packages and partner subscriptions (BE-M5-02).</summary>
public interface ISubscriptionRepository
{
    Task<IReadOnlyList<SubscriptionPackage>> ListPackagesAsync(bool activeOnly, CancellationToken cancellationToken = default);

    Task<SubscriptionPackage?> GetPackageAsync(int packageId, CancellationToken cancellationToken = default);

    /// <summary>The package, TRACKED, so the caller's unit of work saves an Admin edit.</summary>
    Task<SubscriptionPackage?> GetPackageForUpdateAsync(int packageId, CancellationToken cancellationToken = default);

    Task<SubscriptionPackage?> GetPackageByCodeAsync(string packageCode, CancellationToken cancellationToken = default);

    /// <summary>Status of the agency (<c>PENDING</c>, <c>ACTIVE</c>, <c>SUSPENDED</c>) or null when it does not exist.</summary>
    Task<string?> GetAgencyStatusAsync(int agencyId, CancellationToken cancellationToken = default);

    /// <summary>Every subscription of the agency with its package, newest end date first (any status).</summary>
    Task<IReadOnlyList<(PartnerSubscription Subscription, SubscriptionPackage Package)>> ListSubscriptionsAsync(
        int agencyId, CancellationToken cancellationToken = default);

    /// <summary>The agency's <c>PENDING_PAYMENT</c> subscription for the package, if any (not tracked).</summary>
    Task<PartnerSubscription?> FindPendingAsync(int agencyId, int packageId, CancellationToken cancellationToken = default);

    /// <summary>Stages the row; the unit of work saves it (the id is known after the save).</summary>
    void Add(PartnerSubscription subscription);

    /// <summary>The subscription, TRACKED (so the unit of work saves its status change), or null.</summary>
    Task<PartnerSubscription?> GetForUpdateAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Workers of the agency that count against the quota (everything except <c>LOCKED</c>), with their creation time.</summary>
    Task<IReadOnlyList<(int WorkerId, DateTime CreatedAt)>> ListCountedWorkersAsync(int agencyId, CancellationToken cancellationToken = default);

    /// <summary>Ids of agencies that have at least one counted worker (the expiry sweep looks at these).</summary>
    Task<IReadOnlyList<int>> ListAgenciesWithWorkersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the given workers to <c>LOCKED</c> (never deleted); a worker who is <c>BUSY</c> (on site) is left for the next sweep.
    /// Returns how many were locked.
    /// </summary>
    Task<int> LockWorkersAsync(IReadOnlyCollection<int> workerIds, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>The PENDING payment of the subscription (not tracked), or null.</summary>
    Task<PaymentTransaction?> FindPendingPaymentAsync(int subscriptionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Subscription packages (BE-M5-02, decisions Q08, contract agencies.md): list, state and quota, purchase through the payment gateway,
/// the Admin edit with its audit rows, and the quota guard other features call before adding workers.
/// </summary>
public sealed class SubscriptionService(
    ISubscriptionRepository repository,
    IPaymentRepository payments,
    IPaymentGateway gateway,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    ICurrentUser currentUser,
    IClock clock,
    IOptions<BusinessRules> rules,
    ILogger<SubscriptionService>? logger = null) : IWorkerQuotaGuard
{
    private readonly ILogger<SubscriptionService> _logger = logger ?? NullLogger<SubscriptionService>.Instance;

    private int GraceDays => rules.Value.Subscription.GraceDays;

    private int AgencyId => currentUser.UserId is int id && currentUser.Role == UserRole.Partner
        ? id
        : throw new ForbiddenAccessException("Authenticated partner agency is required.");

    private int AdminId => currentUser.UserId is int id && currentUser.Role == UserRole.Admin
        ? id
        : throw new ForbiddenAccessException("Authenticated admin is required.");

    public async Task<IReadOnlyList<PackageDto>> GetPackagesAsync(CancellationToken cancellationToken = default) =>
        (await repository.ListPackagesAsync(activeOnly: true, cancellationToken)).Select(PackageDto.From).ToList();

    public async Task<IReadOnlyList<PackageDto>> GetAllPackagesAsync(CancellationToken cancellationToken = default) =>
        (await repository.ListPackagesAsync(activeOnly: false, cancellationToken)).Select(PackageDto.From).ToList();

    /// <summary>The package in force for the agency today (Pro in its grace days still counts, decisions Q08).</summary>
    public async Task<EffectivePackage> GetEffectivePackageAsync(int agencyId, CancellationToken cancellationToken = default)
    {
        var free = await repository.GetPackageByCodeAsync(WorkerQuotaPolicy.FreePackageCode, cancellationToken)
            ?? new SubscriptionPackage
            {
                PackageCode = WorkerQuotaPolicy.FreePackageCode,
                PackageName = "Free",
                Tier = "FREE",
                WorkerQuota = WorkerQuotaPolicy.FallbackFreeQuota,
                CommissionRate = WorkerQuotaPolicy.FallbackFreeCommissionRate,
                IsActive = true,
            };
        var subscriptions = await repository.ListSubscriptionsAsync(agencyId, cancellationToken);
        return WorkerQuotaPolicy.Effective(subscriptions, free, clock.LocalToday, GraceDays);
    }

    public async Task<SubscriptionStateDto> GetMineAsync(CancellationToken cancellationToken = default)
    {
        var agencyId = AgencyId;
        var effective = await GetEffectivePackageAsync(agencyId, cancellationToken);
        var count = (await repository.ListCountedWorkersAsync(agencyId, cancellationToken)).Count;
        var p = effective.Package;
        return new SubscriptionStateDto(
            p.Tier,
            PackageDto.From(p),
            effective.Subscription is null ? null : ToDto(effective.Subscription),
            effective.InGrace,
            p.WorkerQuota,
            count,
            p.HasRosterDashboard,
            p.HasAnalytics,
            p.PriorityDispatch);
    }

    /// <summary>
    /// Throws a 409 <see cref="SubscriptionErrorCodes.QuotaExceeded"/> when adding <paramref name="additionalWorkers"/> would push the
    /// agency over the quota in force. Import (BE-M5-03) calls it before it writes anything.
    /// </summary>
    public async Task EnsureCanAddWorkersAsync(int agencyId, int additionalWorkers, CancellationToken cancellationToken = default)
    {
        if (additionalWorkers <= 0)
        {
            return;
        }

        var effective = await GetEffectivePackageAsync(agencyId, cancellationToken);
        var count = (await repository.ListCountedWorkersAsync(agencyId, cancellationToken)).Count;
        if (count + additionalWorkers > effective.Package.WorkerQuota)
        {
            throw new BusinessRuleViolationException(
                $"The {effective.Package.Tier} package allows {effective.Package.WorkerQuota} workers; the agency has {count} and adds {additionalWorkers}.",
                SubscriptionErrorCodes.QuotaExceeded);
        }
    }

    /// <summary>Buy or renew: creates the PENDING_PAYMENT subscription and its payment QR; repeated calls return the same open QR.</summary>
    public async Task<SubscriptionPurchaseDto> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var agencyId = AgencyId;
        if (request.PackageId is not > 0)
        {
            throw new ValidationException("packageId", "packageId is required.");
        }

        var package = await repository.GetPackageAsync(request.PackageId.Value, cancellationToken)
            ?? throw new NotFoundException("Package", request.PackageId.Value);
        if (!package.IsActive || package.Price <= 0 || package.BillingCycle is not (BillingCycles.Monthly or BillingCycles.Quarterly))
        {
            throw new BusinessRuleViolationException("This package cannot be purchased.", SubscriptionErrorCodes.PackageNotPurchasable);
        }

        var status = await repository.GetAgencyStatusAsync(agencyId, cancellationToken)
            ?? throw new NotFoundException("Agency", agencyId);
        if (status == AgencyStatuses.Suspended)
        {
            throw new BusinessRuleViolationException("A suspended agency cannot buy a package.", SubscriptionErrorCodes.AgencySuspended);
        }

        var now = clock.UtcNow;
        var expiresAt = now.AddMinutes(rules.Value.Payments.QrExpiryMinutes);

        var pending = await repository.FindPendingAsync(agencyId, package.PackageId, cancellationToken);
        if (pending is not null)
        {
            var open = await repository.FindPendingPaymentAsync(pending.SubscriptionId, cancellationToken);
            if (open is not null && open.CreatedAt.AddMinutes(rules.Value.Payments.QrExpiryMinutes) > now)
            {
                return ToPurchase(pending, open, open.CreatedAt.AddMinutes(rules.Value.Payments.QrExpiryMinutes));
            }
        }

        var today = clock.LocalToday;
        {
            var subscription = pending ?? new PartnerSubscription
            {
                AgencyId = agencyId,
                PackageId = package.PackageId,
                StartDate = today,
                EndDate = today,
                SubStatus = SubscriptionStatuses.PendingPayment,
                AutoRenew = false,
                CreatedAt = now,
            };
            if (pending is null)
            {
                repository.Add(subscription);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            PaymentQr qr;
            try
            {
                qr = await gateway.CreateQrAsync(
                    new CreatePaymentRequest(
                        PaymentPurpose.Subscription,
                        subscription.SubscriptionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        package.Price,
                        $"{package.PackageName} - agency {agencyId}",
                        expiresAt),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new PaymentGatewayUnavailableException("The payment gateway did not create the QR.", ex);
            }

            var transaction = new PaymentTransaction
            {
                GatewayTxnRef = qr.GatewayTxnRef,
                SubscriptionId = subscription.SubscriptionId,
                Purpose = PaymentPurpose.Subscription,
                Gateway = "MOMO",
                Amount = package.Price,
                TxnStatus = PaymentStatus.Pending,
                QrPayload = qr.QrPayload,
                CreatedAt = now,
            };
            payments.Add(transaction);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Agency {AgencyId} started the purchase of package {PackageCode} (subscription {SubscriptionId}, payment {PaymentId}).",
                agencyId, package.PackageCode, subscription.SubscriptionId, transaction.PaymentId);
            return ToPurchase(subscription, transaction, qr.ExpiresAtUtc, qr.PayUrl);
        }
    }

    /// <summary>
    /// Admin edit of a package (decisions G-5, Q08): price, worker quota and/or commission rate, with a reason; one
    /// ADMIN_AUDIT_LOG row per changed field in the same unit of work.
    /// </summary>
    public async Task<PackageDto> UpdatePackageAsync(int packageId, UpdatePackageRequest request, CancellationToken cancellationToken = default)
    {
        var adminId = AdminId;
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > 255)
        {
            errors["reason"] = ["reason is required and must be at most 255 characters."];
        }

        if (request.Price is null && request.WorkerQuota is null && request.CommissionRate is null)
        {
            errors["body"] = ["Send at least one of price, workerQuota, commissionRate."];
        }

        if (request.Price is { } price && (price < 0 || price != decimal.Truncate(price)))
        {
            errors["price"] = ["price must be a whole number of VND, zero or more."];
        }

        if (request.WorkerQuota is { } quota && quota < 1)
        {
            errors["workerQuota"] = ["workerQuota must be at least 1."];
        }

        if (request.CommissionRate is { } rate && (rate < 0 || rate > 1))
        {
            errors["commissionRate"] = ["commissionRate must be between 0 and 1."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var package = await repository.GetPackageForUpdateAsync(packageId, cancellationToken)
                ?? throw new NotFoundException("Package", packageId);

            async Task Audit(string field, string? oldValue, string? newValue) =>
                await auditLog.WriteAsync(
                    new AuditEntry(AuditActorType.Admin, adminId, "SUBSCRIPTION_PACKAGE", packageId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        field, oldValue, newValue, reason),
                    cancellationToken);

            if (request.Price is { } newPrice && newPrice != package.Price)
            {
                await Audit("price", package.Price.ToString(System.Globalization.CultureInfo.InvariantCulture), newPrice.ToString(System.Globalization.CultureInfo.InvariantCulture));
                package.Price = newPrice;
            }

            if (request.WorkerQuota is { } newQuota && newQuota != package.WorkerQuota)
            {
                await Audit("worker_quota", package.WorkerQuota.ToString(System.Globalization.CultureInfo.InvariantCulture), newQuota.ToString(System.Globalization.CultureInfo.InvariantCulture));
                package.WorkerQuota = newQuota;
            }

            if (request.CommissionRate is { } newRate && newRate != package.CommissionRate)
            {
                await Audit("commission_rate", package.CommissionRate.ToString(System.Globalization.CultureInfo.InvariantCulture), newRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
                package.CommissionRate = newRate;
            }

            return PackageDto.From(package);
        }, cancellationToken);
    }

    private static SubscriptionDto ToDto(PartnerSubscription s) =>
        new(s.SubscriptionId, s.AgencyId, s.PackageId, s.StartDate, s.EndDate, s.SubStatus, s.AutoRenew);

    private static SubscriptionPurchaseDto ToPurchase(PartnerSubscription s, PaymentTransaction t, DateTime expiresAt, string? payUrl = null) =>
        new(ToDto(s), t.PaymentId, t.GatewayTxnRef, t.Amount, t.QrPayload, payUrl, expiresAt);

}

/// <summary>
/// Settles a SUBSCRIPTION payment from the IPN (<see cref="IPaymentPurposeSettlement"/>): PENDING (or late EXPIRED) -&gt; SUCCESS and the
/// subscription ACTIVE in one transaction, then <see cref="SubscriptionActivated"/>. A renewal starts the day after the running one ends.
/// Idempotent: a repeat or a lost race returns false.
/// </summary>
public sealed class SubscriptionPaymentSettlement(
    IPaymentRepository payments,
    ISubscriptionRepository subscriptions,
    IUnitOfWork unitOfWork,
    IPublisher publisher,
    IClock clock,
    ILogger<SubscriptionPaymentSettlement>? logger = null) : IPaymentPurposeSettlement
{
    private readonly ILogger<SubscriptionPaymentSettlement> _logger = logger ?? NullLogger<SubscriptionPaymentSettlement>.Instance;

    public PaymentPurpose Purpose => PaymentPurpose.Subscription;

    public async Task<bool> SettleSuccessAsync(PaymentTransaction transaction, bool wasExpired, string rawBody, CancellationToken cancellationToken = default)
    {
        if (transaction.SubscriptionId is not int subscriptionId)
        {
            _logger.LogError("Subscription payment {PaymentId} has no subscription id.", transaction.PaymentId);
            return false;
        }

        var paidAt = clock.UtcNow;
        var today = clock.LocalToday;
        SubscriptionActivated? activated = null;

        var won = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var marked = wasExpired
                ? await payments.TryMarkSuccessFromExpiredAsync(transaction.PaymentId, paidAt, rawBody, cancellationToken)
                : await payments.TryMarkSuccessAsync(transaction.PaymentId, paidAt, rawBody, cancellationToken);
            if (!marked)
            {
                return false;
            }

            var subscription = await subscriptions.GetForUpdateAsync(subscriptionId, cancellationToken);
            var package = subscription is null ? null : await subscriptions.GetPackageAsync(subscription.PackageId, cancellationToken);
            if (subscription is null || package is null)
            {
                _logger.LogError("Payment {PaymentId} succeeded but subscription {SubscriptionId} or its package does not exist.", transaction.PaymentId, subscriptionId);
                return true;
            }

            if (subscription.SubStatus != SubscriptionStatuses.PendingPayment)
            {
                return true; // already activated by a previous settlement of this subscription
            }

            var running = (await subscriptions.ListSubscriptionsAsync(subscription.AgencyId, cancellationToken))
                .Where(s => s.Subscription.SubStatus == SubscriptionStatuses.Active && s.Subscription.SubscriptionId != subscriptionId)
                .Select(s => (DateOnly?)s.Subscription.EndDate)
                .OrderByDescending(d => d)
                .FirstOrDefault();

            var start = WorkerQuotaPolicy.NextStart(today, running);
            subscription.StartDate = start;
            subscription.EndDate = WorkerQuotaPolicy.EndFor(start, package.BillingCycle);
            subscription.SubStatus = SubscriptionStatuses.Active;

            activated = new SubscriptionActivated(
                subscription.SubscriptionId, subscription.AgencyId, package.PackageId, package.PackageCode,
                start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), subscription.EndDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            return true;
        }, cancellationToken);

        if (won && activated is not null)
        {
            await publisher.Publish(activated, cancellationToken);
        }

        return won;
    }
}
