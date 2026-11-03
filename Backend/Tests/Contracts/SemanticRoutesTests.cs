using System.Reflection;
using CommonService.Application.Common.Models;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.WebAPI.Controllers;
using CommonService.WebAPI.Controllers.Payouts;
using CommonService.WebAPI.Controllers.Workers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CommonService.Tests.Contracts;

/// <summary>REFACTOR-03 (#587): the five endpoints use a noun URI plus the HTTP verb, no verb in the path.</summary>
public class SemanticRoutesTests
{
    private static readonly Type[] Controllers = typeof(NotificationsController).Assembly.GetTypes()
        .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
        .ToArray();

    /// <summary>Every "VERB /route" the API exposes, e.g. "GET api/v1/notifications".</summary>
    private static readonly string[] Endpoints = Controllers
        .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>()
                .Select(a => $"{a.HttpMethods.Single()} {Join(c.GetCustomAttribute<RouteAttribute>()?.Template, a.Template)}")))
        .ToArray();

    private static string Join(string? controllerRoute, string? actionRoute) =>
        string.IsNullOrEmpty(actionRoute) ? controllerRoute ?? "" : $"{controllerRoute}/{actionRoute}";

    [Theory]
    [InlineData("GET api/v1/notifications")]
    [InlineData("POST api/v1/workers")]
    [InlineData("GET api/v1/admin/payout-batches/{batchId:int}/file")]
    [InlineData("PUT api/v1/workers/me/slots/{slotDate}/{shiftCode}")]
    [InlineData("GET api/v1/workers/{workerId:int}/availability")]
    public void The_semantic_endpoint_exists(string endpoint) => Assert.Contains(endpoint, Endpoints);

    [Theory]
    [InlineData("GET api/v1/notifications/poll")]
    [InlineData("POST api/v1/workers/register")]
    [InlineData("GET api/v1/admin/payout-batches/{batchId:int}/export")]
    [InlineData("POST api/v1/workers/me/slots/toggle")]
    [InlineData("GET api/v1/booking/orders/{orderId:long}/workers/{workerId:int}/availability")]
    public void The_old_verb_in_the_path_endpoint_is_gone(string endpoint) => Assert.DoesNotContain(endpoint, Endpoints);

    [Fact]
    public async Task PUT_slot_passes_the_route_date_and_shift_and_the_body_state_to_the_slot_service()
    {
        var slots = new RecordingSlots();
        var controller = new WorkersController(null!, null!, slots, null!, null!, null!, null!, null!);

        var result = await controller.PutSlot(new DateOnly(2026, 11, 20), "SHIFT_MORNING", new PutBookingSlotBody(false), default);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(new ToggleBookingSlotRequest(new DateOnly(2026, 11, 20), "SHIFT_MORNING", false), slots.Received);
    }

    [Fact]
    public void The_availability_endpoint_is_customer_only_and_takes_the_order_from_the_query()
    {
        var method = typeof(WorkerAvailabilityController).GetMethod(nameof(WorkerAvailabilityController.RebookAvailability))!;
        var orderId = method.GetParameters().Single(p => p.Name == "orderId");

        Assert.Equal("CustomerOnly", typeof(WorkerAvailabilityController).GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()!.Policy);
        Assert.NotNull(orderId.GetCustomAttribute<FromQueryAttribute>());
    }

    private sealed class RecordingSlots : IWorkerBookingSlotService
    {
        public ToggleBookingSlotRequest? Received { get; private set; }

        public Task<ApiResponse<IReadOnlyList<BookingSlotResponse>>> GetSlotsAsync(DateOnly? startDate, DateOnly? endDate, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ApiResponse<BookingSlotResponse>> ToggleAsync(ToggleBookingSlotRequest request, CancellationToken ct = default)
        {
            Received = request;
            return Task.FromResult(ApiResponse<BookingSlotResponse>.Ok(
                new BookingSlotResponse(1, 1, request.SlotDate, request.ShiftCode, "08:00", "12:00", request.IsActive), "ok"));
        }
    }
}
