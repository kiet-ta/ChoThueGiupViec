using System.Globalization;
using CommonService.Application.Common.Options;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Admin.Services;

public sealed class SuperFreelancerDto
{
    public int WorkerId { get; init; }
    public bool IsSuperFreelancer { get; init; }
}

public sealed class SuperFreelancerResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IDictionary<string, string[]>? ValidationErrors { get; init; }

    /// <summary>Why a 409 refused the approval (contract admin.md section 2.5).</summary>
    public IReadOnlyList<string>? FailedCriteria { get; init; }

    public SuperFreelancerDto? Data { get; init; }

    public static SuperFreelancerResult Ok(int workerId, bool isSuper) => new()
    {
        Success = true,
        StatusCode = 200,
        Data = new SuperFreelancerDto { WorkerId = workerId, IsSuperFreelancer = isSuper },
    };

    public static SuperFreelancerResult NotFound() => new() { StatusCode = 404, ErrorMessage = "Worker not found." };

    public static SuperFreelancerResult Invalid(IDictionary<string, string[]> errors) => new()
    {
        StatusCode = 400,
        ErrorMessage = "Validation failed",
        ValidationErrors = errors,
    };

    public static SuperFreelancerResult Refused(IReadOnlyList<string> failed) => new()
    {
        StatusCode = 409,
        ErrorMessage = "The worker does not meet the Super-Freelancer criteria.",
        FailedCriteria = failed,
    };
}

public interface ISuperFreelancerService
{
    Task<SuperFreelancerResult> ApproveAsync(int adminId, int workerId, string? reason, CancellationToken cancellationToken = default);

    Task<SuperFreelancerResult> RevokeAsync(int adminId, int workerId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Q12: after a new customer rating, a Super-Freelancer whose average fell below the threshold loses the flag (actor SYSTEM).</summary>
    Task AutoRevokeIfBelowThresholdAsync(int workerId, CancellationToken cancellationToken = default);
}

/// <summary>Approve, revoke and auto-revoke of <c>WORKER.is_super_freelancer</c> (decisions Q12, G-5; contract admin.md 2.5).</summary>
public sealed class SuperFreelancerService(
    ISuperFreelancerRepository workers,
    IWorkerReputation reputation,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<BusinessRules> rules) : ISuperFreelancerService
{
    /// <summary>The <c>WORKER.kyc_status</c> value that means "KYC approved". Not defined elsewhere yet: M4 writes it, align it here.</summary>
    public const string KycApprovedStatus = "APPROVED";

    public const string RatingBelowMinimum = "RATING_BELOW_MINIMUM";
    public const string CompletedJobsBelowMinimum = "COMPLETED_JOBS_BELOW_MINIMUM";
    public const string KycNotApproved = "KYC_NOT_APPROVED";
    public const string UpheldDisputeRecent = "UPHELD_DISPUTE_RECENT";
    public const string NotFreelancer = "NOT_FREELANCER";

    private const int MaxReasonLength = 255; // ADMIN_AUDIT_LOG.reason NVARCHAR(255)
    private const string EntityType = "WORKER";
    private const string FieldName = "is_super_freelancer";

    public async Task<SuperFreelancerResult> ApproveAsync(
        int adminId, int workerId, string? reason, CancellationToken cancellationToken = default)
    {
        var errors = ValidateReason(reason, out var cleanReason);
        if (errors.Count > 0) return SuperFreelancerResult.Invalid(errors);

        var worker = await workers.FindWorkerAsync(workerId, cancellationToken);
        if (worker is null) return SuperFreelancerResult.NotFound();
        if (worker.WorkerType != WorkerType.Freelancer) return SuperFreelancerResult.Refused([NotFreelancer]);
        if (worker.IsSuperFreelancer) return SuperFreelancerResult.Ok(workerId, true); // idempotent: nothing changes, nothing is audited

        var failed = await FailedCriteriaAsync(worker.WorkerId, worker.KycStatus, cancellationToken);
        if (failed.Count > 0) return SuperFreelancerResult.Refused(failed);

        worker.IsSuperFreelancer = true;
        await audit.WriteAsync(
            new AuditEntry(AuditActorType.Admin, adminId, EntityType, workerId.ToString(), FieldName, "false", "true", cleanReason),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken); // the flag and its audit row commit together
        return SuperFreelancerResult.Ok(workerId, true);
    }

    public async Task<SuperFreelancerResult> RevokeAsync(
        int adminId, int workerId, string? reason, CancellationToken cancellationToken = default)
    {
        var errors = ValidateReason(reason, out var cleanReason);
        if (errors.Count > 0) return SuperFreelancerResult.Invalid(errors);

        var worker = await workers.FindWorkerAsync(workerId, cancellationToken);
        if (worker is null) return SuperFreelancerResult.NotFound();
        if (!worker.IsSuperFreelancer) return SuperFreelancerResult.Ok(workerId, false); // idempotent

        worker.IsSuperFreelancer = false;
        await audit.WriteAsync(
            new AuditEntry(AuditActorType.Admin, adminId, EntityType, workerId.ToString(), FieldName, "true", "false", cleanReason),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SuperFreelancerResult.Ok(workerId, false);
    }

    public async Task AutoRevokeIfBelowThresholdAsync(int workerId, CancellationToken cancellationToken = default)
    {
        var worker = await workers.FindWorkerAsync(workerId, cancellationToken);
        if (worker is null || !worker.IsSuperFreelancer) return;

        var current = await reputation.GetAsync(workerId, cancellationToken);
        var threshold = rules.Value.SuperFreelancer.RevokeBelowRating;
        if (current is null || current.RatingAvg >= threshold) return; // exactly the threshold keeps the flag

        worker.IsSuperFreelancer = false;
        await audit.WriteAsync(
            new AuditEntry(AuditActorType.System, null, EntityType, workerId.ToString(), FieldName, "true", "false",
                $"rating below {threshold.ToString("0.00", CultureInfo.InvariantCulture)}"),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<string>> FailedCriteriaAsync(int workerId, string kycStatus, CancellationToken cancellationToken)
    {
        var rule = rules.Value.SuperFreelancer;
        var failed = new List<string>();

        var current = await reputation.GetAsync(workerId, cancellationToken);
        if ((current?.RatingAvg ?? 0m) < rule.MinRating) failed.Add(RatingBelowMinimum);
        if ((current?.CompletedJobs ?? 0) < rule.MinCompletedJobs) failed.Add(CompletedJobsBelowMinimum);
        if (!string.Equals(kycStatus, KycApprovedStatus, StringComparison.OrdinalIgnoreCase)) failed.Add(KycNotApproved);

        var since = clock.UtcNow.AddDays(-rule.NoUpheldDisputeDays);
        if (await workers.HasUpheldFreelancerDisputeSinceAsync(workerId, since, cancellationToken)) failed.Add(UpheldDisputeRecent);

        return failed;
    }

    private static Dictionary<string, string[]> ValidateReason(string? reason, out string clean)
    {
        var errors = new Dictionary<string, string[]>();
        clean = (reason ?? string.Empty).Trim();
        if (clean.Length == 0) errors["reason"] = ["A reason is required."];
        else if (clean.Length > MaxReasonLength) errors["reason"] = [$"The reason must be at most {MaxReasonLength} characters."];
        return errors;
    }
}
