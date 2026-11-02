using CommonService.Application.Features.Agencies;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Agencies.Dtos;

/// <summary>
/// Request to retire an agency worker, optionally with a replacement.
/// </summary>
public sealed class RetireWorkerRequest
{
    /// <summary>
    /// Optional replacement worker. If provided as an existing worker ID, must belong to the same agency
    /// and not be Pending/Locked/Retiring/Retired. If provided as a new worker, uses the same fields
    /// and validation as the import row.
    /// </summary>
    public RetireWorkerReplacement? Replacement { get; set; }

    /// <summary>
    /// Confirm that future jobs should be handed to emergency rescue (with SLA penalty).
    /// Required when the worker has future ASSIGNED jobs and no replacement is provided.
    /// </summary>
    public bool ConfirmRescue { get; set; }
}

/// <summary>
/// Replacement worker for retirement - either an existing worker or a new worker to create.
/// </summary>
public sealed class RetireWorkerReplacement
{
    /// <summary>Existing worker ID of the same agency.</summary>
    public int? ExistingWorkerId { get; set; }

    /// <summary>New worker details (same validation as import).</summary>
    public NewWorkerDetails? NewWorker { get; set; }
}

/// <summary>
/// New worker details for replacement (same validation as import row).
/// </summary>
public sealed class NewWorkerDetails
{
    /// <summary>Full name (required, max 100 chars).</summary>
    public string? FullName { get; set; }

    /// <summary>Phone number (required, Vietnamese mobile format, stored as 0XXXXXXXXX).</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>National ID (required, 12 digits CCCD).</summary>
    public string? NationalId { get; set; }

    /// <summary>Bank name (optional, max 100 chars).</summary>
    public string? BankName { get; set; }

    /// <summary>Bank account number (optional, 6-30 digits, spaces ignored).</summary>
    public string? BankAccountNo { get; set; }
}

/// <summary>
/// Response for retiring a worker.
/// </summary>
public sealed class RetireWorkerResponse
{
    /// <summary>The worker ID that was retired.</summary>
    public int WorkerId { get; set; }

    /// <summary>
    /// True for the preview: the worker has future jobs, no replacement and no <c>confirmRescue</c>. Nothing was changed;
    /// <see cref="FutureJobs"/> lists the shifts the agency must decide about.
    /// </summary>
    public bool RequiresConfirmation { get; set; }

    /// <summary>The work status (Retiring or Retired; the unchanged status for a preview or a repeated call).</summary>
    public string WorkStatus { get; set; } = string.Empty;

    /// <summary>If a replacement was provided, the new worker ID.</summary>
    public int? ReplacementWorkerId { get; set; }

    /// <summary>Future jobs that were handed over, or that need a decision in a preview (if any).</summary>
    public IReadOnlyList<FutureJobHandover>? FutureJobs { get; set; }

    /// <summary>Warning message if the worker was put in Retiring state (still has active/future jobs).</summary>
    public string? Warning { get; set; }
}