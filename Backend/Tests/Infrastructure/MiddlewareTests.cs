using System.Text.Json;
using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Infrastructure;

public class MiddlewareTests
{
    private static DefaultHttpContext CreateContext(string path = "/api/test", string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadResponseBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    // =========================================================================
    // ErrorHandlingMiddleware Tests
    // =========================================================================

    [Fact]
    public async Task ErrorHandlingMiddleware_handles_ValidationException_as_400_with_errors()
    {
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new ValidationException("phoneNumber", "Invalid Vietnamese phone format."),
            NullLogger<ErrorHandlingMiddleware>.Instance);

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Contains("application/json", context.Response.ContentType);

        var body = await ReadResponseBodyAsync(context);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("Validation failed", root.GetProperty("message").GetString());

        var errors = root.GetProperty("data").GetProperty("errors");
        Assert.True(errors.TryGetProperty("phoneNumber", out var phoneErrors));
        Assert.Equal("Invalid Vietnamese phone format.", phoneErrors[0].GetString());
    }

    [Fact]
    public async Task ErrorHandlingMiddleware_handles_NotFoundException_as_404()
    {
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new NotFoundException("Customer", 42),
            NullLogger<ErrorHandlingMiddleware>.Instance);

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);

        var body = await ReadResponseBodyAsync(context);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Contains("42", root.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task ErrorHandlingMiddleware_handles_ForbiddenAccessException_as_403()
    {
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new ForbiddenAccessException("You do not own this address."),
            NullLogger<ErrorHandlingMiddleware>.Instance);

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        var body = await ReadResponseBodyAsync(context);
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("You do not own this address.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ErrorHandlingMiddleware_handles_BusinessRuleViolationException_as_409()
    {
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new BusinessRuleViolationException("Slot already booked"),
            NullLogger<ErrorHandlingMiddleware>.Instance);

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);

        var body = await ReadResponseBodyAsync(context);
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Slot already booked", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ErrorHandlingMiddleware_handles_unexpected_exception_as_500_without_leaking_message()
    {
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new InvalidOperationException("Sensitive DB connection failed: password=secret"),
            NullLogger<ErrorHandlingMiddleware>.Instance);

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);

        var body = await ReadResponseBodyAsync(context);
        Assert.DoesNotContain("Sensitive DB", body);
        Assert.DoesNotContain("secret", body);

        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("An unexpected error occurred. Please try again later.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ErrorHandlingMiddleware_returns_ProblemDetails_when_requested()
    {
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new NotFoundException("Worker", 99),
            NullLogger<ErrorHandlingMiddleware>.Instance);

        var context = CreateContext();
        context.Request.Headers.Accept = "application/problem+json";

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Contains("application/problem+json", context.Response.ContentType);

        var body = await ReadResponseBodyAsync(context);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(404, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Not Found", doc.RootElement.GetProperty("title").GetString());
    }

    // =========================================================================
    // ResponseWrapperMiddleware Tests
    // =========================================================================

    [Fact]
    public async Task ResponseWrapperMiddleware_wraps_successful_json_response_with_camelCase_ApiResponse()
    {
        var middleware = new ResponseWrapperMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"name\":\"Nguyen Van A\",\"age\":30}");
        });

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        var body = await ReadResponseBodyAsync(context);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("Request successful", root.GetProperty("message").GetString());
        Assert.Equal("Nguyen Van A", root.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ResponseWrapperMiddleware_wraps_non_2xx_with_success_false()
    {
        var middleware = new ResponseWrapperMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"error\":\"Bad request\"}");
        });

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        var body = await ReadResponseBodyAsync(context);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("Request failed", root.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ResponseWrapperMiddleware_does_not_double_wrap_already_wrapped_response()
    {
        var middleware = new ResponseWrapperMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            ctx.Response.ContentType = "application/json";
            var existing = ApiResponse<string>.Ok("Already wrapped value", "Existing message");
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(existing));
        });

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        var body = await ReadResponseBodyAsync(context);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        // Verify 'data' is the string itself, not an inner wrapper
        Assert.Equal("Already wrapped value", root.GetProperty("data").GetString());
    }

    [Fact]
    public async Task ResponseWrapperMiddleware_bypasses_swagger_and_hubs()
    {
        var swaggerInvoked = false;
        var swaggerMiddleware = new ResponseWrapperMiddleware(ctx =>
        {
            swaggerInvoked = true;
            ctx.Response.ContentType = "application/json";
            return ctx.Response.WriteAsync("{\"openapi\":\"3.0.1\"}");
        });

        var swaggerContext = CreateContext(path: "/swagger/v1/swagger.json");
        await swaggerMiddleware.InvokeAsync(swaggerContext);

        Assert.True(swaggerInvoked);
        var swaggerBody = await ReadResponseBodyAsync(swaggerContext);
        Assert.Equal("{\"openapi\":\"3.0.1\"}", swaggerBody);

        var hubInvoked = false;
        var hubMiddleware = new ResponseWrapperMiddleware(ctx =>
        {
            hubInvoked = true;
            return Task.CompletedTask;
        });

        var hubContext = CreateContext(path: "/hubs/notifications");
        await hubMiddleware.InvokeAsync(hubContext);
        Assert.True(hubInvoked);
    }
}
