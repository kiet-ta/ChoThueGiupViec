using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Admin.Services;

public sealed class WorkerUnlockDto
{
    public int WorkerId { get; init; }
    public string WorkStatus { get; init; } = string.Empty;
}

public sealed class WorkerUnlockResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IDictionary<string, string[]>? ValidationErrors { get; init; }

    /// <summary>Why a 409 refused the unlock (contract admin.md section 2.6).</summary>
    public IReadOnlyList<string>? BlockReasons { get; init; }

    public WorkerUnlockDto? Data { get; init; }

    public static WorkerUnlockResult Ok(int workerId, string workStatus) => new()
    {
        Success = true,
        StatusCode = 200,
        Data = new WorkerUnlockDto { WorkerId = workerId, WorkStatus = workStatus },
    };

    public static WorkerUnlockResult NotFound() => new() { StatusCode = 404, ErrorMessage = "Worker not found." };

    public static WorkerUnlockResult Invalid(IDictionary<string, string[]> errors) => new()
    {
        StatusCode = 400,
        ErrorMessage = "Validation failed",
        ValidationErrors = errors,
    };

    public static WorkerUnlockResult Refused(IReadOnlyList<string> reasons) => new()
    {
        StatusCode = 409,
        ErrorMessage = "The worker cannot be unlocked.",
        BlockReasons = reasons,
    };
}

public interface IWorkerUnlockService
{
    Task<WorkerUnlockResult> UnlockAsync(int adminId, int workerId, string? reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// Admin unlock of a locked freelancer (decisions Q15 "Admin can unlock", G-5; contract admin.md 2.6). The lock itself comes from a
/// dispute verdict (BE-M6-02c), the cancellation rule (Q15) or a rejected eKYC. Only a freelancer whose eKYC is approved goes back to
/// IDLE, so the unlock is never a way around eKYC; agency staff are locked by quota and unlocked by their agency.
/// </summary>
public sealed class WorkerUnlockService(IWorkerLockRepository workers, IAuditLog audit, IUnitOfWork unitOfWork) : IWorkerUnlockService
{
    public const string NotFreelancer = "NOT_FREELANCER";
    public const string NotLocked = "NOT_LOCKED";
    public const string KycNotApproved = "KYC_NOT_APPROVED";

    private const int MaxReasonLength = 255; // ADMIN_AUDIT_LOG.reason NVARCHAR(255)
    private const string EntityType = "WORKER";
    private const string FieldName = "work_status";

    public async Task<WorkerUnlockResult> UnlockAsync(int adminId, int workerId, string? reason, CancellationToken cancellationToken = default)
    {
        var clean = (reason ?? string.Empty).Trim();
        var errors = new Dictionary<string, string[]>();
        if (clean.Length == 0) errors["reason"] = ["A reason is required."];
        else if (clean.Length > MaxReasonLength) errors["reason"] = [$"The reason must be at most {MaxReasonLength} characters."];
        if (errors.Count > 0) return WorkerUnlockResult.Invalid(errors);

        var worker = await workers.FindWorkerAsync(workerId, cancellationToken);
        if (worker is null) return WorkerUnlockResult.NotFound();

        var blocked = new List<string>();
        if (worker.WorkerType != WorkerType.Freelancer) blocked.Add(NotFreelancer);
        if (worker.WorkStatus != WorkStatus.Locked) blocked.Add(NotLocked);
        if (!string.Equals(worker.KycStatus, SuperFreelancerService.KycApprovedStatus, StringComparison.OrdinalIgnoreCase)) blocked.Add(KycNotApproved);
        if (blocked.Count > 0) return WorkerUnlockResult.Refused(blocked);

        worker.Unlock();
        await audit.WriteAsync(
            new AuditEntry(AuditActorType.Admin, adminId, EntityType, workerId.ToString(), FieldName,
                DbEnum.ToDb(WorkStatus.Locked), DbEnum.ToDb(worker.WorkStatus), clean),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken); // the status and its audit row commit together
        return WorkerUnlockResult.Ok(workerId, DbEnum.ToDb(worker.WorkStatus));
    }
}
