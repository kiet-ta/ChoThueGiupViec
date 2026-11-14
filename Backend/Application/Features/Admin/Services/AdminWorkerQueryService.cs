using System.Globalization;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Admin.Dtos;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Admin.Services;

/// <summary>GET /api/v1/admin/workers rules of contract admin.md section 2.8 (ADMIN-UAT-14).</summary>
public class AdminWorkerQueryService(IAdminWorkerSearchRepository repository) : IAdminWorkerQueryService
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
    public const int MaxTextLength = 100;

    public async Task<AdminWorkerPageDto> SearchAsync(
        string? q, string? workerType, string? workStatus, string? page, string? pageSize, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();

        var pageNumber = ParseInt(page, 1, 1, int.MaxValue, "page", "Page must be an integer of at least 1.", errors);
        var size = ParseInt(pageSize, DefaultPageSize, 1, MaxPageSize, "pageSize",
            $"Page size must be an integer between 1 and {MaxPageSize}.", errors);

        var text = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        if (text is { Length: > MaxTextLength })
        {
            errors["q"] = [$"The search text must be at most {MaxTextLength} characters."];
        }

        WorkerType? type = null;
        if (!string.IsNullOrWhiteSpace(workerType))
        {
            type = ParseEnum<WorkerType>(workerType);
            if (type is null) errors["workerType"] = ["Worker type must be FREELANCER or AGENCY_STAFF."];
        }

        WorkStatus? status = null;
        if (!string.IsNullOrWhiteSpace(workStatus))
        {
            status = ParseEnum<WorkStatus>(workStatus);
            if (status is null)
            {
                errors["workStatus"] = [$"Work status must be one of: {string.Join(", ", Enum.GetValues<WorkStatus>().Select(DbEnum.ToDb))}."];
            }
        }

        if (errors.Count > 0) throw new ValidationException("Validation failed", errors);

        // A whole number is also a worker number: that worker is matched on top of the text match.
        int? id = text is not null && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1 ? parsed : null;

        var (rows, total) = await repository.SearchAsync(new AdminWorkerFilter(text, id, type, status), pageNumber, size, cancellationToken);

        return new AdminWorkerPageDto
        {
            Items = rows.Select(ToDto).ToList(),
            Page = pageNumber,
            PageSize = size,
            Total = total,
        };
    }

    private static int ParseInt(string? raw, int fallback, int min, int max, string field, string message, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (int.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max)
        {
            return value;
        }

        errors[field] = [message];
        return fallback;
    }

    private static TEnum? ParseEnum<TEnum>(string raw) where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(DbEnum.ToDb(candidate), raw.Trim(), StringComparison.OrdinalIgnoreCase)) return candidate;
        }

        return null;
    }

    private static AdminWorkerDto ToDto(AdminWorkerRow row) => new()
    {
        WorkerId = row.WorkerId,
        FullName = row.FullName,
        PhoneNumber = row.PhoneNumber,
        NationalId = row.NationalId,
        WorkerType = DbEnum.ToDb(row.WorkerType),
        AgencyId = row.AgencyId,
        AgencyName = row.AgencyName,
        WorkStatus = DbEnum.ToDb(row.WorkStatus),
        KycStatus = row.KycStatus,
        IsSuperFreelancer = row.IsSuperFreelancer,
        RatingAvg = row.RatingAvg,
        CompletedJobs = row.CompletedJobs,
    };
}
