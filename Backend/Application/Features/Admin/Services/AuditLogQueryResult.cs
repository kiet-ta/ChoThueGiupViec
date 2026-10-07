using CommonService.Application.Features.Admin.Dtos;

namespace CommonService.Application.Features.Admin.Services;

public sealed class AuditLogQueryResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IDictionary<string, string[]>? ValidationErrors { get; init; }
    public AuditLogPageDto? Data { get; init; }

    public static AuditLogQueryResult Ok(AuditLogPageDto data) => new() { Success = true, StatusCode = 200, Data = data };

    public static AuditLogQueryResult ValidationError(IDictionary<string, string[]> errors) => new()
    {
        Success = false,
        StatusCode = 400,
        ErrorMessage = "Validation failed",
        ValidationErrors = errors,
    };
}
