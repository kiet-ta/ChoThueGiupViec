using System.Text.Json;
using CommonService.Application.Exceptions;
using CommonService.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Middleware;

/// <summary>
/// The three middlewares composed in the order of Program.cs (error handling, logging, wrapper). Each one alone passed its tests while the
/// composition returned 500 with an empty body on a real server: the logging middleware left its disposed buffer as the response body when an
/// exception passed through it.
/// </summary>
public class MiddlewarePipelineTests
{
    private static RequestDelegate Pipeline(RequestDelegate endpoint)
    {
        var wrapper = new ResponseWrapperMiddleware(endpoint);
        var logging = new RequestResponseLoggingMiddleware(wrapper.InvokeAsync, NullLogger<RequestResponseLoggingMiddleware>.Instance);
        var errors = new ErrorHandlingMiddleware(logging.Invoke, NullLogger<ErrorHandlingMiddleware>.Instance);
        return errors.InvokeAsync;
    }

    private static async Task<(DefaultHttpContext Context, byte[] Body)> SendAsync(RequestDelegate endpoint, string path = "/api/test")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "GET";
        var sink = new MemoryStream();
        context.Response.Body = sink;

        await Pipeline(endpoint)(context);
        return (context, sink.ToArray());
    }

    [Theory]
    [InlineData(typeof(ValidationException), StatusCodes.Status400BadRequest)]
    [InlineData(typeof(NotFoundException), StatusCodes.Status404NotFound)]
    [InlineData(typeof(BusinessRuleViolationException), StatusCodes.Status409Conflict)]
    public async Task A_thrown_application_exception_reaches_the_client_with_its_status_and_the_envelope(Type exceptionType, int status)
    {
        var (context, body) = await SendAsync(_ => throw (Exception)(exceptionType == typeof(ValidationException)
            ? new ValidationException("field", "bad")
            : exceptionType == typeof(NotFoundException)
                ? new NotFoundException("Thing", 7)
                : new BusinessRuleViolationException("rule", "SOME_CODE"))!);

        Assert.Equal(status, context.Response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task A_file_response_keeps_its_exact_bytes_and_headers()
    {
        var file = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();

        var (context, body) = await SendAsync(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            ctx.Response.Headers.ContentDisposition = "attachment; filename=a.xlsx";
            ctx.Response.ContentLength = file.Length;
            await ctx.Response.Body.WriteAsync(file);
        });

        Assert.Equal(file, body);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", context.Response.ContentType);
    }

    [Fact]
    public async Task A_json_response_is_still_wrapped_in_the_envelope()
    {
        var (_, body) = await SendAsync(async ctx =>
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"n\":1}");
        });

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, doc.RootElement.GetProperty("data").GetProperty("n").GetInt32());
    }
}
