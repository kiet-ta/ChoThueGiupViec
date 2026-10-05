namespace CommonService.Application.Exceptions;

/// <summary>
/// Exception thrown when the authenticated user does not have permission to access a resource.
/// Maps to HTTP 403 Forbidden.
/// </summary>
public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException()
        : base("Access to this resource is forbidden.")
    {
    }

    public ForbiddenAccessException(string message)
        : base(message)
    {
    }

    public ForbiddenAccessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
