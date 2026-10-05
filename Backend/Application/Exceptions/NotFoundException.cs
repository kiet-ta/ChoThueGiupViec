namespace CommonService.Application.Exceptions;

/// <summary>
/// Exception thrown when a requested entity or resource is not found.
/// Maps to HTTP 404 Not Found.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException()
        : base("The requested resource was not found.")
    {
    }

    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public NotFoundException(string entityName, object key)
        : base($"Resource \"{entityName}\" ({key}) was not found.")
    {
    }
}
