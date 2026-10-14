using CommonService.Application.Common.Models;
using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Booking.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Booking;

/// <summary>Customer booking endpoints (contract booking.md 3.1 to 3.6). Policy CustomerOnly; someone else's order or address is 404.</summary>
[ApiController]
[Route("api/booking")]
[Authorize(Policy = "CustomerOnly")]
public class BookingOrdersController(IOrderQueryService service, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Static booking choices (tiers, shifts, limits) so the app hard-codes nothing.</summary>
    [HttpGet("options")]
    [ProducesResponseType(typeof(ApiResponse<BookingOptionsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public IActionResult Options() => Ok(ApiResponse<BookingOptionsDto>.Ok(service.GetOptions(), "Booking options retrieved."));

    /// <summary>The fixed price before paying. Read only: the price is frozen only when the order is created.</summary>
    [HttpGet("price-quote")]
    [ProducesResponseType(typeof(ApiResponse<PriceQuoteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<IActionResult> PriceQuote([FromQuery] int addressId, [FromQuery] string? serviceTier, CancellationToken ct) =>
        AsCustomer(async id => Ok(ApiResponse<PriceQuoteDto>.Ok(await service.QuoteAsync(id, addressId, serviceTier, ct), "Price quote retrieved.")));

    /// <summary>Creates a PENDING_PAYMENT order; the client then asks for its QR.</summary>
    [HttpPost("orders")]
    [ProducesResponseType(typeof(ApiResponse<OrderDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Create([FromBody] CreateOrderBody body, CancellationToken ct) =>
        AsCustomer(async id => StatusCode(
            StatusCodes.Status201Created, ApiResponse<OrderDto>.Ok(await service.CreateAsync(id, body, ct), "Order created.")));

    /// <summary>The caller's orders, newest first, optionally one status; <c>pageSize</c> 1-100, default 20.</summary>
    [HttpGet("orders")]
    [ProducesResponseType(typeof(ApiResponse<OrderPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> List([FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        AsCustomer(async id => Ok(ApiResponse<OrderPageDto>.Ok(await service.ListAsync(id, status, page, pageSize, ct), "Orders retrieved.")));

    /// <summary>One order of the caller.</summary>
    [HttpGet("orders/{orderId:long}")]
    [ProducesResponseType(typeof(ApiResponse<OrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Get(long orderId, CancellationToken ct) =>
        AsCustomer(async id => Ok(ApiResponse<OrderDto>.Ok(await service.GetAsync(id, orderId, ct), "Order retrieved.")));

    /// <summary>The order's status and its visible assignments (polling fallback of the realtime <c>job.tracking</c> topic).</summary>
    [HttpGet("orders/{orderId:long}/progress")]
    [ProducesResponseType(typeof(ApiResponse<OrderProgressDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Progress(long orderId, CancellationToken ct) =>
        AsCustomer(async id => Ok(ApiResponse<OrderProgressDto>.Ok(await service.GetProgressAsync(id, orderId, ct), "Order progress retrieved.")));

    private async Task<IActionResult> AsCustomer(Func<int, Task<IActionResult>> action) =>
        currentUser.UserId is int customerId
            ? await action(customerId)
            : Unauthorized(ApiResponse<object>.Fail("Unauthorized."));
}
