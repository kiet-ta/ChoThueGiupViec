using CommonService.Application.Common.Helpers;
using CommonService.Application.Exceptions;

namespace CommonService.Tests;

/// <summary>What a client sees from a service call: the data, or the status ErrorHandlingMiddleware gives the thrown exception.</summary>
public record ServiceOutcome<T>(
    int StatusCode, T? Data, string? ErrorMessage, IDictionary<string, string[]>? ValidationErrors, int? RetryAfterSeconds)
{
    public bool Success => StatusCode < 300;
}

public static class ServiceOutcomeExtensions
{
    /// <summary>Awaits the call; an application exception becomes its HTTP status (same mapping as ProblemDetailsHelper), any other one is rethrown.</summary>
    public static async Task<ServiceOutcome<T>> Outcome<T>(this Task<T> call)
    {
        try
        {
            return new ServiceOutcome<T>(200, await call, null, null, null);
        }
        catch (Exception ex) when (ProblemDetailsHelper.FromException(ex).Status is int status && status != 500)
        {
            return new ServiceOutcome<T>(
                status,
                default,
                ex.Message,
                (ex as ValidationException)?.Errors,
                (ex as AccountLockedException)?.RetryAfterSeconds);
        }
    }
}
