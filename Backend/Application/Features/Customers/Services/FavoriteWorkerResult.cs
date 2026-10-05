namespace CommonService.Application.Features.Customers.Services;

public sealed class FavoriteWorkerResult<T>
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public T? Data { get; init; }

    public static FavoriteWorkerResult<T> Ok(T data) => new()
    {
        Success = true,
        StatusCode = 200,
        Data = data
    };

    /// <summary>No worker with that id (decided by IWorkerProfileQuery), or the customer row is gone.</summary>
    public static FavoriteWorkerResult<T> NotFound(string message) => new()
    {
        Success = false,
        StatusCode = 404,
        ErrorMessage = message
    };
}
