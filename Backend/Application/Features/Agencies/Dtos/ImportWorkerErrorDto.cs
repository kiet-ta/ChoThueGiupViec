namespace CommonService.Application.Features.Agencies.Dtos;

/// <summary>
/// Error object for worker import operation
/// </summary>
public sealed record ImportWorkerErrorDto
{
    /// <summary>Line number in the CSV file (1-based)</summary>
    public int LineNumber { get; init; }

    /// <summary>Field that had the validation error</summary>
    public string? FieldName { get; init; }

    /// <summary>Error message</summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>The value that caused the error</summary>
    public string? InvalidValue { get; init; }
}