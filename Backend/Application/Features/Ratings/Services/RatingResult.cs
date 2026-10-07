namespace CommonService.Application.Features.Ratings.Services;

public sealed class RatingResult<T>
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IDictionary<string, string[]>? ValidationErrors { get; init; }
    public T? Data { get; init; }

    public static RatingResult<T> Ok(T data) => new() { Success = true, StatusCode = 200, Data = data };

    public static RatingResult<T> Created(T data) => new() { Success = true, StatusCode = 201, Data = data };

    public static RatingResult<T> ValidationError(IDictionary<string, string[]> errors) => new()
    {
        Success = false,
        StatusCode = 400,
        ErrorMessage = "Validation failed",
        ValidationErrors = errors,
    };

    /// <summary>The assignment does not exist or is not the caller's: one answer, so nothing leaks.</summary>
    public static RatingResult<T> NotFound() => new()
    {
        Success = false,
        StatusCode = 404,
        ErrorMessage = "Assignment not found.",
    };

    /// <summary><paramref name="reason"/> is the RatingWindow reason (NOT_COMPLETED, WINDOW_CLOSED, ALREADY_RATED).</summary>
    public static RatingResult<T> Conflict(string reason) => new()
    {
        Success = false,
        StatusCode = 409,
        ErrorMessage = reason switch
        {
            "NOT_COMPLETED" => "The assignment is not completed yet.",
            "WINDOW_CLOSED" => "The rating window has closed.",
            "ALREADY_RATED" => "This assignment has already been rated.",
            _ => "Rating is not possible.",
        },
    };
}
