namespace CommonService.Application.Exceptions;

/// <summary>
/// Exception thrown when an operation violates a business rule or invariant.
/// Maps to HTTP 409 Conflict (or 422 Unprocessable Entity).
/// </summary>
public class BusinessRuleViolationException : Exception
{
    public string? Code { get; }

    public BusinessRuleViolationException()
        : base("A business rule violation occurred.")
    {
    }

    public BusinessRuleViolationException(string message, string? code = null)
        : base(message)
    {
        Code = code;
    }

    public BusinessRuleViolationException(string message, Exception innerException, string? code = null)
        : base(message, innerException)
    {
        Code = code;
    }
}
