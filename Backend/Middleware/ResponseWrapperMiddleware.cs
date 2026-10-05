using System.Text;
using System.Text.Json;
using CommonService.Application.Common.Models;

namespace CommonService.Middleware;

/// <summary>
/// Wraps all JSON responses in the universal ApiResponse envelope with camelCase property naming.
/// Does not double-wrap if the response is already in ApiResponse format, while normalizing keys to camelCase.
/// Automatically flags non-2xx responses with success = false.
/// Skips Swagger, health checks, SignalR hubs, and static file endpoints.
/// </summary>
public class ResponseWrapperMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly RequestDelegate _next;

    public ResponseWrapperMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (ShouldBypass(path))
        {
            await _next(context);
            return;
        }

        var originalBodyStream = context.Response.Body;
        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;

        try
        {
            await _next(context);

            memoryStream.Seek(0, SeekOrigin.Begin);
            var responseBody = await new StreamReader(memoryStream).ReadToEndAsync();

            context.Response.Body = originalBodyStream;

            // If empty 204 No Content
            if (context.Response.StatusCode == StatusCodes.Status204NoContent)
            {
                return;
            }

            var statusCode = context.Response.StatusCode;
            var isSuccessStatus = statusCode >= 200 && statusCode < 300;

            // If already wrapped in ApiResponse, normalize to camelCase and avoid re-wrapping
            if (IsAlreadyApiResponse(responseBody, out var existingSuccess, out var existingMessage, out var existingData))
            {
                context.Response.ContentType = "application/json; charset=utf-8";
                var normalizedObj = new
                {
                    success = isSuccessStatus ? existingSuccess : false,
                    message = existingMessage,
                    data = existingData
                };
                var normalizedJson = JsonSerializer.Serialize(normalizedObj, JsonOptions);
                await context.Response.WriteAsync(normalizedJson, Encoding.UTF8);
                return;
            }

            object? wrappedResponse;

            if (isSuccessStatus)
            {
                if (string.IsNullOrWhiteSpace(responseBody))
                {
                    wrappedResponse = ApiResponse<object?>.Ok(null, "Request successful");
                }
                else
                {
                    try
                    {
                        var data = JsonSerializer.Deserialize<object>(responseBody);
                        wrappedResponse = ApiResponse<object>.Ok(data ?? new { }, "Request successful");
                    }
                    catch
                    {
                        wrappedResponse = ApiResponse<string>.Ok(responseBody, "Request successful");
                    }
                }
            }
            else
            {
                // Non-2xx status: MUST have success = false
                if (string.IsNullOrWhiteSpace(responseBody))
                {
                    wrappedResponse = ApiResponse<object?>.Fail("Request failed", null);
                }
                else
                {
                    try
                    {
                        var data = JsonSerializer.Deserialize<object>(responseBody);
                        wrappedResponse = ApiResponse<object>.Fail("Request failed", data);
                    }
                    catch
                    {
                        wrappedResponse = ApiResponse<string>.Fail(responseBody, null);
                    }
                }
            }

            context.Response.ContentType = "application/json; charset=utf-8";
            var wrappedJson = JsonSerializer.Serialize(wrappedResponse, JsonOptions);
            await context.Response.WriteAsync(wrappedJson, Encoding.UTF8);
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    private static bool ShouldBypass(PathString path)
    {
        return path.StartsWithSegments("/swagger") ||
               path.StartsWithSegments("/api-docs") ||
               path.StartsWithSegments("/health") ||
               path.StartsWithSegments("/healthz") ||
               path.StartsWithSegments("/hubs") ||
               path.StartsWithSegments("/files");
    }

    private static bool IsAlreadyApiResponse(string json, out bool success, out string message, out object? data)
    {
        success = false;
        message = string.Empty;
        data = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var root = doc.RootElement;
            var foundSuccess = false;

            if (root.TryGetProperty("success", out var sProp) || root.TryGetProperty("Success", out sProp))
            {
                if (sProp.ValueKind == JsonValueKind.True || sProp.ValueKind == JsonValueKind.False)
                {
                    success = sProp.GetBoolean();
                    foundSuccess = true;
                }
            }

            var foundData = false;
            if (root.TryGetProperty("data", out var dProp) || root.TryGetProperty("Data", out dProp))
            {
                foundData = true;
                if (dProp.ValueKind == JsonValueKind.Null)
                {
                    data = null;
                }
                else
                {
                    data = JsonSerializer.Deserialize<object>(dProp.GetRawText());
                }
            }

            if (foundSuccess && foundData)
            {
                if (root.TryGetProperty("message", out var mProp) || root.TryGetProperty("Message", out mProp))
                {
                    message = mProp.GetString() ?? string.Empty;
                }
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
