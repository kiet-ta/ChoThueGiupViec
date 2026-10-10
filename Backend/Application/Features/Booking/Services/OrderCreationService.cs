using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Booking.Services;

/// <summary>
/// Creates a PENDING_PAYMENT order (BE-M2-03, contract booking.md 3.3). The price is frozen into total_amount (Q01).
/// PREMIUM: the order is inserted and the capacity reserved in ONE transaction, keyed by the order id (B8); no capacity -> rollback, FULLY_BOOKED.
/// </summary>
public sealed class OrderCreationService(
    ICustomerAddressQuery addresses,
    IPricingService pricing,
    IOrderRepository orders,
    IAgencyCapacityService capacity,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<BusinessRules> rules) : IOrderCreationService
{
    private const int MaxNoteLength = 500;
    private const int MaxSkillLength = 100;
    private const int OrderCodeAttempts = 5;
    private readonly BusinessRules _rules = rules.Value;

    public async Task<CreatedOrder> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var address = await addresses.GetOwnedAsync(request.CustomerId, request.AddressId, cancellationToken)
            ?? throw new NotFoundException("Address", request.AddressId);

        var nowUtc = clock.UtcNow;
        var shiftStartUtc = BookingWindowPolicy.GetShiftStartUtc(request.ScheduledDate, request.ShiftCode, clock)!.Value;
        var windowError = BookingWindowPolicy.Check(request.ServiceTier, shiftStartUtc, nowUtc, _rules.Premium.MinLeadHours);
        if (windowError is not null)
        {
            throw new BusinessRuleViolationException(
                windowError == BookingErrorCodes.ShiftInPast
                    ? "The shift starts in the past."
                    : $"A Premium order must be booked at least {_rules.Premium.MinLeadHours} hours before the shift starts.",
                windowError);
        }

        var plan = ShiftPlanner.Plan(address.TotalAreaM2, _rules);
        var quote = await pricing.QuoteAsync(request.ServiceTier, address.TotalAreaM2, plan.RequiredWorkers, cancellationToken);
        var orderCode = await NewOrderCodeAsync(cancellationToken);
        var shiftEndUtc = ToUtc(request.ScheduledDate, BookingShifts.GetLocalTimes(request.ShiftCode)!.Value.End);

        CapacityReservation? reservation = null;
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var order = new JobOrder
                {
                    OrderCode = orderCode,
                    CustomerId = request.CustomerId,
                    AddressId = request.AddressId,
                    ServiceTier = request.ServiceTier,
                    ScheduledDate = request.ScheduledDate,
                    ShiftCode = request.ShiftCode,
                    AreaSnapshotM2 = address.TotalAreaM2,
                    RequiredWorkers = (byte)plan.RequiredWorkers,
                    RequiredSkill = request.RequiredSkill,
                    TotalAmount = quote.TotalAmount,
                    CustomerNote = request.CustomerNote,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc,
                };
                orders.Add(order);
                await unitOfWork.SaveChangesAsync(cancellationToken);

                if (request.ServiceTier == ServiceTier.Premium)
                {
                    reservation = await capacity.TryReserveAsync(
                        new CapacityRequest(request.ScheduledDate, request.ShiftCode, plan.RequiredWorkers, order.OrderId),
                        cancellationToken);
                    if (reservation is null)
                    {
                        throw new BusinessRuleViolationException("No partner agency has capacity for this shift.", BookingErrorCodes.FullyBooked);
                    }
                }

                return new CreatedOrder(
                    order.OrderId, order.OrderCode, order.ServiceTier, order.AddressId, order.ScheduledDate, order.ShiftCode,
                    shiftStartUtc, shiftEndUtc, order.AreaSnapshotM2, order.RequiredWorkers, order.RequiredSkill, order.TotalAmount,
                    order.OrderStatus, order.CustomerNote, order.CancelReason,
                    nowUtc.AddMinutes(_rules.Payments.QrExpiryMinutes), order.CreatedAt, order.UpdatedAt);
            }, cancellationToken);
        }
        catch
        {
            // The hold must not outlive an order that was rolled back (e.g. the commit failed after the reservation).
            if (reservation is not null)
            {
                await capacity.ReleaseAsync(reservation.ReservationId, CancellationToken.None);
            }

            throw;
        }
    }

    private DateTime ToUtc(DateOnly date, TimeOnly time) =>
        clock.ToUtc(DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified));

    private async Task<string> NewOrderCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < OrderCodeAttempts; attempt++)
        {
            var code = OrderCodeGenerator.Create(clock.LocalToday, Random.Shared.Next);
            if (!await orders.OrderCodeExistsAsync(code, cancellationToken))
            {
                return code;
            }
        }

        throw new InvalidOperationException($"Could not generate a unique order_code in {OrderCodeAttempts} attempts.");
    }

    private void Validate(CreateOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (request.AddressId <= 0)
        {
            errors["addressId"] = ["addressId must be greater than 0."];
        }

        if (!Enum.IsDefined(request.ServiceTier))
        {
            errors["serviceTier"] = ["serviceTier must be ECONOMY or PREMIUM."];
        }

        if (BookingShifts.GetLocalTimes(request.ShiftCode) is null)
        {
            errors["shiftCode"] = [$"shiftCode must be {BookingShifts.Morning}, {BookingShifts.Afternoon} or {BookingShifts.Evening}."];
        }

        if (request.ScheduledDate > clock.LocalToday.AddDays(_rules.Booking.MaxDaysAhead))
        {
            errors["scheduledDate"] = [$"scheduledDate must be at most {_rules.Booking.MaxDaysAhead} days ahead."];
        }

        if (request.CustomerNote is { Length: > MaxNoteLength })
        {
            errors["customerNote"] = [$"customerNote must be at most {MaxNoteLength} characters."];
        }

        if (request.RequiredSkill is { Length: > MaxSkillLength })
        {
            errors["requiredSkill"] = [$"requiredSkill must be at most {MaxSkillLength} characters."];
        }
        else if (!string.IsNullOrWhiteSpace(request.RequiredSkill) && request.ServiceTier != ServiceTier.Premium)
        {
            errors["requiredSkill"] = ["requiredSkill is only allowed for PREMIUM orders."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }
}
