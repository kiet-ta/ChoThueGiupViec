using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Admin;

/// <param name="Text">Substring of the full name, phone number or national id; null for no text filter.</param>
/// <param name="WorkerId">The number when the text is a whole number: that worker is also matched (and listed first).</param>
/// <param name="WorkerType">Exact worker type; null for any.</param>
/// <param name="WorkStatus">Exact work status; null for any.</param>
public record AdminWorkerFilter(string? Text, int? WorkerId, WorkerType? WorkerType, WorkStatus? WorkStatus);

/// <summary>One row of the lookup; the agency name comes from an explicit join because Worker has no navigation properties.</summary>
public record AdminWorkerRow(
    int WorkerId,
    string FullName,
    string PhoneNumber,
    string NationalId,
    WorkerType WorkerType,
    int? AgencyId,
    string? AgencyName,
    WorkStatus WorkStatus,
    string KycStatus,
    bool IsSuperFreelancer,
    decimal RatingAvg,
    int CompletedJobs);

/// <summary>Read side of WORKER for the Admin console (ADMIN-UAT-14). Read-only: no tracking, no write method.</summary>
public interface IAdminWorkerSearchRepository
{
    /// <summary>Exact id match first, then the newest worker first. <paramref name="page"/> is 1-based.</summary>
    Task<(IReadOnlyList<AdminWorkerRow> Items, int Total)> SearchAsync(
        AdminWorkerFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);
}
