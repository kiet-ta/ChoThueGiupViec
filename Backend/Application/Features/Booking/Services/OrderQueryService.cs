using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Booking.Services;

/// <summary>The customer's read side of Booking and the HTTP-facing order creation (contract booking.md 3.1 to 3.6).</summary>
public interface IOrderQueryService
{
    BookingOptionsDto GetOptions();

    /// <summary>ValidationException (400), NotFoundException (404 address not the customer's). Creates nothing.</summary>
    Task<PriceQuoteDto> QuoteAsync(int customerId, int addressId, string? serviceTier, CancellationToken cancellationToken = default);

    /// <summary>Checks the body, then <see cref="IOrderCreationService"/> (its 400 / 404 / 409 are unchanged).</summary>
    Task<OrderDto> CreateAsync(int customerId, CreateOrderBody body, CancellationToken cancellationToken = default);

    /// <summary>ValidationException (400) on an unknown status or bad paging.</summary>
    Task<OrderPageDto> ListAsync(int customerId, string? status, int? page, int? pageSize, CancellationToken cancellationToken = default);

    /// <summary>NotFoundException (404) when the order is not the customer's.</summary>
    Task<OrderDto> GetAsync(int customerId, long orderId, CancellationToken cancellationToken = default);

    /// <summary>NotFoundException (404) when the order is not the customer's.</summary>
    Task<OrderProgressDto> GetProgressAsync(int customerId, long orderId, CancellationToken cancellationToken = default);
}

public sealed class OrderQueryService(
    IOrderRepository orders,
    IOrderTrackingQuery tracking,
    IExtensionRepository extensions,
    ICustomerAddressQuery addresses,
    IPricingService pricing,
    IOrderCreationService creation,
    IWorkerProfileQuery workers,
    IClock clock,
    IOptions<BusinessRules> rules) : IOrderQueryService
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    private const string Currency = "VND";

    /// <summary>A customer never sees who was merely offered the job, nor cancelled or reassigned seats (contract section 2, Q22 D1).</summary>
    private static readonly HashSet<JobAssignmentStatus> Hidden =
        [JobAssignmentStatus.Offered, JobAssignmentStatus.Cancelled, JobAssignmentStatus.Reassigned];

    private readonly BusinessRules _rules = rules.Value;

    public BookingOptionsDto GetOptions() => new(
        Enum.GetValues<ServiceTier>().Select(DbEnum.ToDb).ToList(),
        new[] { BookingShifts.Morning, BookingShifts.Afternoon, BookingShifts.Evening }
            .Select(code =>
            {
                var (start, end) = BookingShifts.GetLocalTimes(code)!.Value;
                return new ShiftOptionDto(code, start.ToString("HH:mm"), end.ToString("HH:mm"));
            })
            .ToList(),
        _rules.Premium.MinLeadHours,
        _rules.Shift.MaxHours,
        _rules.Area.StandardMaxM2,
        _rules.Payments.QrExpiryMinutes,
        Sandbox: true);

    public async Task<PriceQuoteDto> QuoteAsync(int customerId, int addressId, string? serviceTier, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (addressId <= 0)
        {
            errors["addressId"] = ["addressId must be greater than 0."];
        }

        if (!TryParseTier(serviceTier, out var tier))
        {
            errors["serviceTier"] = ["serviceTier must be ECONOMY or PREMIUM."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var address = await addresses.GetOwnedAsync(customerId, addressId, cancellationToken)
            ?? throw new NotFoundException("Address", addressId);
        var plan = ShiftPlanner.Plan(address.TotalAreaM2, _rules);
        var quote = await pricing.QuoteAsync(tier, address.TotalAreaM2, plan.RequiredWorkers, cancellationToken);

        return new PriceQuoteDto(
            addressId, DbEnum.ToDb(tier), quote.TotalAreaM2, quote.AreaBracket, quote.RequiredWorkers, quote.UnitPrice, quote.TotalAmount, Currency);
    }

    public async Task<OrderDto> CreateAsync(int customerId, CreateOrderBody body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (!TryParseTier(body.ServiceTier, out var tier))
        {
            errors["serviceTier"] = ["serviceTier must be ECONOMY or PREMIUM."];
        }

        if (body.ScheduledDate is null)
        {
            errors["scheduledDate"] = ["scheduledDate is required (yyyy-MM-dd)."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var created = await creation.CreateAsync(
            new CreateOrderRequest(customerId, body.AddressId, tier, body.ScheduledDate!.Value, body.ShiftCode ?? string.Empty, body.CustomerNote, body.RequiredSkill),
            cancellationToken);
        return OrderDto.From(created);
    }

    public async Task<OrderPageDto> ListAsync(int customerId, string? status, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        JobOrderStatus? filter = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (TryParseStatus(status, out var parsed))
            {
                filter = parsed;
            }
            else
            {
                errors["status"] = ["status is not an order status."];
            }
        }

        var pageNumber = page ?? 1;
        var size = pageSize ?? DefaultPageSize;
        if (pageNumber < 1)
        {
            errors["page"] = ["page must be at least 1."];
        }

        if (size is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var (items, total) = await orders.ListByCustomerAsync(customerId, filter, (pageNumber - 1) * size, size, cancellationToken);
        return new OrderPageDto(items.Select(OrderMapper.ToSummary).ToList(), pageNumber, size, total);
    }

    public async Task<OrderDto> GetAsync(int customerId, long orderId, CancellationToken cancellationToken = default)
    {
        var order = await orders.GetOwnedAsync(customerId, orderId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);
        return OrderDto.From(OrderMapper.ToOrder(order, clock, _rules));
    }

    public async Task<OrderProgressDto> GetProgressAsync(int customerId, long orderId, CancellationToken cancellationToken = default)
    {
        var order = await orders.GetOwnedAsync(customerId, orderId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);

        var visible = (await tracking.GetOrderProgressAsync(orderId, cancellationToken))
            .Where(a => !Hidden.Contains(a.AssignmentStatus))
            .ToList();

        // Names and ratings come from the Workers port, never from the WORKER table (contract section 2, Q21 C3).
        var profiles = visible.Count == 0
            ? new Dictionary<int, WorkerProfileSummary>()
            : await workers.GetManyAsync(visible.Select(a => a.WorkerId).Distinct(), cancellationToken);

        var assignments = visible
            .Select(a =>
            {
                profiles.TryGetValue(a.WorkerId, out var profile);
                return new ProgressAssignmentDto(
                    a.AssignmentId, a.AssignmentSeq, a.WorkerId, profile?.FullName, profile?.RatingAvg ?? 0m,
                    DbEnum.ToDb(a.AssignmentStatus), a.AcceptedAt, a.CompletedAt);
            })
            .ToList();

        var extension = await extensions.GetByOrderAsync(orderId, cancellationToken);
        var extensionDto = extension is null
            ? null
            : new ExtensionDto(
                extension.ExtensionId, extension.OrderId, extension.WorkerId, extension.ExtraHours, extension.ExtraAmount,
                extension.ExtStatus, extension.WorkerDecision, extension.RequestedAt, extension.DecidedAt);

        return new OrderProgressDto(order.OrderId, DbEnum.ToDb(order.OrderStatus), order.RequiredWorkers, assignments, extensionDto);
    }

    /// <summary>Exactly the contract's spelling (ECONOMY, PREMIUM); anything else is not a tier.</summary>
    private static bool TryParseTier(string? text, out ServiceTier tier)
    {
        foreach (var value in Enum.GetValues<ServiceTier>())
        {
            if (DbEnum.ToDb(value) == text)
            {
                tier = value;
                return true;
            }
        }

        tier = default;
        return false;
    }

    private static bool TryParseStatus(string text, out JobOrderStatus status)
    {
        foreach (var value in Enum.GetValues<JobOrderStatus>())
        {
            if (DbEnum.ToDb(value) == text)
            {
                status = value;
                return true;
            }
        }

        status = default;
        return false;
    }
}
