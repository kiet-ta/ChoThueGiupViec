using CommonService.Application.Common.Models;
using System.Text;
using System.Text.Json;

namespace CommonService.Middleware;
public class ResponseWrapperMiddleware
{
    private readonly RequestDelegate _next;

    public ResponseWrapperMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Bắt response gốc
        var originalBodyStream = context.Response.Body;

        using var newBodyStream = new MemoryStream();
        context.Response.Body = newBodyStream;

        await _next(context);

        // Reset về đầu để đọc response
        newBodyStream.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(newBodyStream).ReadToEndAsync();
        newBodyStream.Seek(0, SeekOrigin.Begin);

        object? responseObj;

        // Nếu response là JSON hợp lệ thì wrap
        if (!string.IsNullOrWhiteSpace(responseBody) &&
            (context.Response.ContentType?.Contains("application/json") ?? false))
        {
            try
            {
                var data = JsonSerializer.Deserialize<object>(responseBody);
                responseObj = ApiResponse<object>.Ok(data ?? new { }, "Request successful");
            }
            catch
            {
                responseObj = ApiResponse<string>.Ok(responseBody, "Raw response");
            }
        }
        else
        {
            // Response không phải JSON → wrap thẳng
            responseObj = ApiResponse<string>.Ok(responseBody, "Non-JSON response");
        }

        // Ghi response mới
        context.Response.Body = originalBodyStream;
        context.Response.ContentType = "application/json";
        var wrapped = JsonSerializer.Serialize(responseObj);
        await context.Response.WriteAsync(wrapped, Encoding.UTF8);
    }
}
