namespace CommonService.Application.Exceptions;

/// <summary>
/// Exception thrown when a port outside the module (refund, SLA penalty, ...) refused or failed; nothing was saved.
/// Maps to HTTP 502 Bad Gateway.
/// </summary>
public class BadGatewayException : Exception
{
    public BadGatewayException(string message)
        : base(message)
    {
    }

    public BadGatewayException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
