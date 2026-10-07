namespace CommonService.Application.Features.Disputes.Services;

public sealed class DisputeResult<T>
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IDictionary<string, string[]>? ValidationErrors { get; init; }
    public T? Data { get; init; }

    public static DisputeResult<T> Ok(T data) => new() { Success = true, StatusCode = 200, Data = data };

    public static DisputeResult<T> Created(T data) => new() { Success = true, StatusCode = 201, Data = data };

    public static DisputeResult<T> ValidationError(IDictionary<string, string[]> errors) => new()
    {
        StatusCode = 400,
        ErrorMessage = "Validation failed",
        ValidationErrors = errors,
    };

    /// <summary>Missing, or not the caller's: one answer, so nothing leaks.</summary>
    public static DisputeResult<T> NotFound() => new() { StatusCode = 404, ErrorMessage = "Not found." };

    public static DisputeResult<T> Conflict(string message) => new() { StatusCode = 409, ErrorMessage = message };
}
