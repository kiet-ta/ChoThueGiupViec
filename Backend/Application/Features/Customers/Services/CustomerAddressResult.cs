namespace CommonService.Application.Features.Customers.Services;

public sealed class CustomerAddressResult<T>
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IDictionary<string, string[]>? ValidationErrors { get; init; }
    public T? Data { get; init; }

    public static CustomerAddressResult<T> Ok(T data) => new()
    {
        Success = true,
        StatusCode = 200,
        Data = data
    };

    public static CustomerAddressResult<T> Created(T data) => new()
    {
        Success = true,
        StatusCode = 201,
        Data = data
    };

    public static CustomerAddressResult<T> ValidationError(IDictionary<string, string[]> errors) => new()
    {
        Success = false,
        StatusCode = 400,
        ErrorMessage = "Validation failed",
        ValidationErrors = errors
    };

    /// <summary>The address does not exist or is not owned by the caller (no existence leak).</summary>
    public static CustomerAddressResult<T> NotFound(string message = "Address not found.") => new()
    {
        Success = false,
        StatusCode = 404,
        ErrorMessage = message
    };

    public static CustomerAddressResult<T> Conflict(string message) => new()
    {
        Success = false,
        StatusCode = 409,
        ErrorMessage = message
    };
}
