using System.Net;
using System.Text.Json;
using CommonService.Application.Common.Helpers;
using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using Microsoft.Extensions.Logging;

namespace CommonService.Middleware;

/// <summary>
/// Global exception handling middleware.
/// Maps domain/application exceptions to standard HTTP status codes and formats responses
/// according to contract conventions (ApiResponse in camelCase) or RFC 7807 ProblemDetails when requested.
/// </summary>
public class ErrorHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                _logger.LogWarning(ex, "The response has already started, cannot modify error headers.");
                throw;
            }

            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        var (statusCode, apiResponse, problemDetails) = MapException(ex, context.Request.Path);

        if (statusCode >= 500)
        {
            _logger.LogError(ex, "Unhandled server error processing {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning("Request failed with {StatusCode} ({ExceptionType}) on {Method} {Path}: {Message}",
                statusCode, ex.GetType().Name, context.Request.Method, context.Request.Path, ex.Message);
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;

        var acceptHeader = context.Request.Headers.Accept.ToString();
        var wantsProblemJson = acceptHeader.Contains("application/problem+json", StringComparison.OrdinalIgnoreCase);

        if (wantsProblemJson)
        {
            context.Response.ContentType = "application/problem+json; charset=utf-8";
            await JsonSerializer.SerializeAsync(context.Response.Body, problemDetails, JsonOptions);
        }
        else
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            await JsonSerializer.SerializeAsync(context.Response.Body, apiResponse, JsonOptions);
        }
    }

    private static (int StatusCode, ApiResponse<object> ApiResponse, Microsoft.AspNetCore.Mvc.ProblemDetails Problem)
        MapException(Exception ex, string path)
    {
        var problem = ProblemDetailsHelper.FromException(ex, path);

        return ex switch
        {
            ValidationException valEx => (
                StatusCodes.Status400BadRequest,
                ApiResponse<object>.Fail("Validation failed", new { errors = valEx.Errors }),
                problem
            ),
            ArgumentException argEx => (
                StatusCodes.Status400BadRequest,
                ApiResponse<object>.Fail(argEx.Message),
                problem
            ),
            UnauthorizedAccessException => (
                StatusCodes.Status401Unauthorized,
                ApiResponse<object>.Fail("Unauthorized"),
                problem
            ),
            ForbiddenAccessException faEx => (
                StatusCodes.Status403Forbidden,
                ApiResponse<object>.Fail(string.IsNullOrWhiteSpace(faEx.Message) ? "Forbidden" : faEx.Message),
                problem
            ),
            NotFoundException nfEx => (
                StatusCodes.Status404NotFound,
                ApiResponse<object>.Fail(string.IsNullOrWhiteSpace(nfEx.Message) ? "Resource not found" : nfEx.Message),
                problem
            ),
            KeyNotFoundException knfEx => (
                StatusCodes.Status404NotFound,
                ApiResponse<object>.Fail(string.IsNullOrWhiteSpace(knfEx.Message) ? "Resource not found" : knfEx.Message),
                problem
            ),
            BusinessRuleViolationException brEx => (
                StatusCodes.Status409Conflict,
                // A business 409 carries data: { code } so clients branch on the code, never on the message (contracts booking.md 3.3,
                // payments.md 2.1). Without a code the answer is unchanged (data null).
                ApiResponse<object>.Fail(
                    string.IsNullOrWhiteSpace(brEx.Message) ? "Business rule violation" : brEx.Message,
                    brEx.Code is null ? null : new { code = brEx.Code }),
                problem
            ),
            _ => (
                StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Fail("An unexpected error occurred. Please try again later."),
                problem
            )
        };
    }
}