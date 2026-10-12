using System.Text.Json;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Booking.Services;
using CommonService.Application.Features.Payments.Services;
using CommonService.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CommonService.Tests.Booking;

/// <summary>
/// BE-M2-13: a business 409 must carry <c>data: { "code": "..." }</c> on the wire (contracts booking.md 3.3, 3.7, 3.8; payments.md 2.1),
/// so clients branch on the code and never on the message. These tests read the actual JSON the middleware writes.
/// </summary>
public sealed class BusinessCodeOnTheWireTests
{
    private static async Task<(int Status, JsonElement Body)> WriteAsync(Exception thrown, string accept = "application/json")
    {
        var middleware = new ErrorHandlingMiddleware(_ => throw thrown, NullLogger<ErrorHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Headers.Accept = accept;
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        return (context.Response.StatusCode, JsonDocument.Parse(json).RootElement.Clone());
    }

    [Theory]
    [InlineData(BookingErrorCodes.ShiftInPast)]
    [InlineData(BookingErrorCodes.PremiumLeadTime)]
    [InlineData(BookingErrorCodes.FullyBooked)]
    [InlineData(CancelErrorCodes.InvalidState)]
    [InlineData(ExtensionErrorCodes.ExtensionExists)]
    [InlineData(PaymentErrorCodes.PaymentExpired)]
    public async Task ABusiness409_CarriesItsCodeInData(string code)
    {
        var (status, body) = await WriteAsync(new BusinessRuleViolationException("Readable message", code));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("Readable message", body.GetProperty("message").GetString());
        Assert.Equal(code, body.GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task A409WithoutACode_IsUnchanged_DataIsNull()
    {
        var (status, body) = await WriteAsync(new BusinessRuleViolationException("Slot already booked"));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Equal("Slot already booked", body.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task OtherErrors_AreUnchanged()
    {
        var (notFound, notFoundBody) = await WriteAsync(new NotFoundException("Order", 42));
        var (validation, validationBody) = await WriteAsync(new ValidationException("reason", "required"));

        Assert.Equal(StatusCodes.Status404NotFound, notFound);
        Assert.Equal(JsonValueKind.Null, notFoundBody.GetProperty("data").ValueKind);
        Assert.Equal(StatusCodes.Status400BadRequest, validation);
        Assert.Equal("required", validationBody.GetProperty("data").GetProperty("errors").GetProperty("reason")[0].GetString());
    }
}
