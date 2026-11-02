namespace CommonService.Middleware;

public class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestResponseLoggingMiddleware> _logger;

    private const string Redacted = "[redacted]";

    // Header values that authenticate a caller or a payment webhook never go to the log.
    private static readonly HashSet<string> SecretHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Set-Cookie", "X-SePay-Signature",
    };

    private static Dictionary<string, string> LoggableHeaders(IHeaderDictionary headers) =>
        headers.ToDictionary(h => h.Key, h => SecretHeaders.Contains(h.Key) ? Redacted : h.Value.ToString());

    public RequestResponseLoggingMiddleware(RequestDelegate next, ILogger<RequestResponseLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        // Log request
        _logger.LogInformation("Incoming Request: {method} {url} {headers}",
            context.Request.Method,
            context.Request.Path,
            LoggableHeaders(context.Request.Headers));

        //Copy response body to log it
        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);

            // Log Response
            // Seek: go to the begining of the stream
            responseBody.Seek(0, SeekOrigin.Begin);
            var responseText = await new StreamReader(responseBody, leaveOpen: true).ReadToEndAsync();
            //Seek again to the begining of the stream for next middleware
            responseBody.Seek(0, SeekOrigin.Begin);

            // Log response details, if not log, log will empty
            _logger.LogInformation("Outgoing Response: {statusCode} {headers} {body}",
                context.Response.StatusCode,
                LoggableHeaders(context.Response.Headers),
                responseText);

            // Copy the contents of the new memory stream (which contains the response) to the original stream, which is then returned to the client.
            await responseBody.CopyToAsync(originalBodyStream);
        }
        finally
        {
            // An exception passes through here to ErrorHandlingMiddleware (outer): it must write to the real body, not to the
            // buffer that is disposed when this method ends.
            context.Response.Body = originalBodyStream;
        }
    }
}
