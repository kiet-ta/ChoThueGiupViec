using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.Application.Common.Helpers;

/// <summary>
/// Helper to construct RFC 7807 ProblemDetails and convert to/from ApiResponse envelopes.
/// </summary>
public static class ProblemDetailsHelper
{
    public static ProblemDetails CreateProblemDetails(
        int statusCode,
        string title,
        string? detail = null,
        string? instance = null,
        IDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = instance,
            Type = GetRfcTypeForStatus(statusCode)
        };

        if (extensions != null)
        {
            foreach (var (key, value) in extensions)
            {
                problem.Extensions[key] = value;
            }
        }

        return problem;
    }

    public static ValidationProblemDetails CreateValidationProblemDetails(
        IDictionary<string, string[]> errors,
        string? detail = "One or more validation errors occurred.",
        string? instance = null)
    {
        var problem = new ValidationProblemDetails(errors)
        {
            Status = 400,
            Title = "One or more validation errors occurred.",
            Detail = detail,
            Instance = instance,
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1"
        };
        return problem;
    }

    public static ProblemDetails FromException(Exception ex, string? instance = null)
    {
        return ex switch
        {
            ValidationException valEx => CreateValidationProblemDetails(valEx.Errors, valEx.Message, instance),
            NotFoundException nfEx => CreateProblemDetails(404, "Not Found", nfEx.Message, instance),
            ForbiddenAccessException faEx => CreateProblemDetails(403, "Forbidden", faEx.Message, instance),
            UnauthorizedAccessException uaEx => CreateProblemDetails(401, "Unauthorized", uaEx.Message, instance),
            BusinessRuleViolationException brEx => CreateProblemDetails(
                409,
                "Conflict",
                brEx.Message,
                instance,
                brEx.Code != null ? new Dictionary<string, object?> { ["code"] = brEx.Code } : null),
            ArgumentException argEx => CreateProblemDetails(400, "Bad Request", argEx.Message, instance),
            _ => CreateProblemDetails(500, "Internal Server Error", "An unexpected error occurred. Please try again later.", instance)
        };
    }

    public static ApiResponse<object> ToApiResponse(ProblemDetails problem)
    {
        if (problem is ValidationProblemDetails valProblem)
        {
            return ApiResponse<object>.Fail(valProblem.Title ?? "Validation failed", new { errors = valProblem.Errors });
        }

        return ApiResponse<object>.Fail(problem.Detail ?? problem.Title ?? "Request failed");
    }

    private static string GetRfcTypeForStatus(int statusCode) => statusCode switch
    {
        400 => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
        401 => "https://tools.ietf.org/html/rfc7235#section-3.1",
        403 => "https://tools.ietf.org/html/rfc7231#section-6.5.3",
        404 => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
        409 => "https://tools.ietf.org/html/rfc7231#section-6.5.8",
        422 => "https://tools.ietf.org/html/rfc4918#section-11.2",
        429 => "https://tools.ietf.org/html/rfc6585#section-4",
        _ => "https://tools.ietf.org/html/rfc7231#section-6.6.1"
    };
}
