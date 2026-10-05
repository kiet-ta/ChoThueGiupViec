namespace CommonService.Application.Common.Models;

/// <summary>
/// Universal response envelope for all API endpoints (per contract conventions).
/// Shape: { "success": bool, "message": string, "data": T | null }.
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "")
    {
        return new ApiResponse<T>
        {
            Success = true,
            Message = message,
            Data = data
        };
    }

    public static ApiResponse<T> Fail(string message, T? data = default)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            Data = data
        };
    }
}

/// <summary>
/// Non-generic convenience helpers for ApiResponse.
/// </summary>
public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string message = "") => ApiResponse<T>.Ok(data, message);

    public static ApiResponse<T> Fail<T>(string message, T? data = default) => ApiResponse<T>.Fail(message, data);

    public static ApiResponse<object?> Fail(string message) => ApiResponse<object?>.Fail(message, null);
}
