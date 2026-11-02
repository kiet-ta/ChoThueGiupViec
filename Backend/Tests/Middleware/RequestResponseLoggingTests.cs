using System.Text;
using CommonService.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CommonService.Tests.Middleware;

public class RequestResponseLoggingTests
{
    private sealed class CapturingLogger : ILogger<RequestResponseLoggingMiddleware>
    {
        public List<string> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Add(formatter(state, exception));
    }

    private static async Task<(CapturingLogger Logger, string ClientBody)> RunAsync()
    {
        var logger = new CapturingLogger();
        var middleware = new RequestResponseLoggingMiddleware(async ctx =>
        {
            ctx.Response.Headers["Set-Cookie"] = "session=top-secret-cookie";
            await ctx.Response.WriteAsync("hello");
        }, logger);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Method = "POST";
        context.Request.Headers["Authorization"] = "Bearer top-secret-token";
        context.Request.Headers["Cookie"] = "a=top-secret-cookie";
        context.Request.Headers["x-sepay-signature"] = "sha256=top-secret-signature";
        context.Request.Headers["Accept"] = "application/json";
        var sink = new MemoryStream();
        context.Response.Body = sink;

        await middleware.Invoke(context);
        return (logger, Encoding.UTF8.GetString(sink.ToArray()));
    }

    [Fact]
    public async Task SecretHeaders_AreRedactedInTheLog()
    {
        var (logger, _) = await RunAsync();
        var log = string.Join("\n", logger.Lines);

        Assert.DoesNotContain("top-secret", log);
        Assert.Contains("[redacted]", log);
    }

    [Fact]
    public async Task OtherHeaders_AreStillLogged_AndTheClientStillGetsTheBody()
    {
        var (logger, clientBody) = await RunAsync();

        Assert.Contains("application/json", string.Join("\n", logger.Lines));
        Assert.Equal("hello", clientBody);
    }
}
