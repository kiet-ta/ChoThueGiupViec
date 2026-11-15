using System.Globalization;
using CommonService.Application.Common.Options;
using CommonService.Application.Features.Booking;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Dispatch;

/// <summary>A live Premium order with its seats (decision D38).</summary>
/// <param name="HasOpenSwapWindow">An agency swap window is open on one of its assignments (D13 handles it).</param>
/// <param name="HasIncident">One of its assignments is an INCIDENT or ABSENT (BR-10 / BR-05 handle it).</param>
public record PremiumOrderSnapshot(
    long OrderId, int CustomerId, DateOnly ScheduledDate, string ShiftCode, int RequiredWorkers, decimal TotalAmount,
    bool HasOpenSwapWindow, bool HasIncident, IReadOnlyList<PremiumSeat> Seats);

/// <summary>Reads the live Premium orders the shift-start sweep judges, and the order status after the cancel.</summary>
public interface IPremiumStartRepository
{
    /// <summary>PAID, DISPATCHING and ASSIGNED PREMIUM orders scheduled on or before <paramref name="latestDate"/>, with their live seats.</summary>
    Task<IReadOnlyList<PremiumOrderSnapshot>> GetLivePremiumOrdersAsync(DateOnly latestDate, CancellationToken cancellationToken = default);

    Task<JobOrderStatus?> GetOrderStatusAsync(long orderId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Decision D38 (PREM-START-01): a Premium order whose seats are not all present at the shift start + grace is cancelled: the agencies of the
/// absent seats are penalised first (-30 SLA points per order and the seat price per absent worker from the escrow; the penalty service is
/// idempotent, so a failure is retried on the next tick), then <see cref="AssignmentFailed"/> cancels the order, refunds 100 % and cancels every
/// assignment (the workers go back to their agency), and the customer gets an in-app apology.
/// </summary>
public class PremiumShiftStartService(
    IPremiumStartRepository repository,
    ISlaPenaltyService slaPenalties,
    IPublisher publisher,
    INotificationService notifications,
    IClock clock,
    IOptions<BusinessRules> rules,
    ILogger<PremiumShiftStartService>? logger = null)
{
    public const string ApologyTopic = "premium.order_cancelled";

    private readonly ILogger<PremiumShiftStartService> _logger = logger ?? NullLogger<PremiumShiftStartService>.Instance;

    /// <summary>Returns how many orders were cancelled by this run.</summary>
    public async Task<int> CancelUnstaffedOrdersAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var premium = rules.Value.Premium;
        var orders = await repository.GetLivePremiumOrdersAsync(clock.LocalToday, cancellationToken);
        var cancelled = 0;
        foreach (var order in orders)
        {
            if (order.HasOpenSwapWindow || order.HasIncident)
            {
                continue;
            }

            var start = BookingWindowPolicy.GetShiftStartUtc(order.ScheduledDate, order.ShiftCode, clock);
            if (start is null)
            {
                continue;
            }

            var (absent, unfilled) = PremiumShiftStartRules.Evaluate(order.Seats, order.RequiredWorkers, start.Value, now, premium);
            if (absent.Count == 0 && unfilled == 0)
            {
                continue;
            }

            try
            {
                if (await CancelAsync(order, absent, premium.CheckInGraceMinutes, now, cancellationToken))
                {
                    cancelled++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The penalty is idempotent and nothing was cancelled yet when it failed: the next tick tries again.
                _logger.LogError(ex, "Could not cancel the unstaffed Premium order {OrderId}; it is retried at the next tick.", order.OrderId);
            }
        }

        return cancelled;
    }

    private async Task<bool> CancelAsync(
        PremiumOrderSnapshot order, IReadOnlyList<PremiumSeat> absent, int graceMinutes, DateTime now, CancellationToken cancellationToken)
    {
        var reason = $"Đối tác chưa đủ người làm đúng giờ (đơn Premium {order.OrderId}).";

        // 1. The penalty first (idempotent: agency + order + reason). One penalty per agency: -30 points and the seat price per absent worker.
        foreach (var byAgency in absent.Where(s => s.AgencyId is not null).GroupBy(s => s.AgencyId!.Value))
        {
            await slaPenalties.ApplyAsync(
                new SlaPenaltyRequest(
                    byAgency.Key, SlaViolation.NoShowAtStart, order.OrderId, null, byAgency.Sum(s => s.GrossAmount), 0m,
                    $"{byAgency.Count()} worker(s) not present {graceMinutes} min after the shift start (order {order.OrderId})."),
                cancellationToken);
        }

        foreach (var freelancer in absent.Where(s => s.AgencyId is null))
        {
            // The violation of a Super-Freelancer is PREM-RESCUE-01; here the seat is only logged.
            _logger.LogWarning(
                "Order {OrderId}: the rescue worker {WorkerId} of seat {Seq} was not present in time.", order.OrderId, freelancer.WorkerId, freelancer.Seq);
        }

        // 2. Cancel the order: refund 100 %, release the hold, cancel every assignment.
        await publisher.Publish(new AssignmentFailed(order.OrderId, reason, now), cancellationToken);

        // 3. The apology, only when the order is really cancelled now (otherwise the next tick repeats the whole step).
        if (await repository.GetOrderStatusAsync(order.OrderId, cancellationToken) != JobOrderStatus.Cancelled)
        {
            return false;
        }

        var amount = order.TotalAmount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        try
        {
            await notifications.SendAsync(
                new NotificationMessage(
                    UserRole.Customer, order.CustomerId, ApologyTopic, "TỔ ẤM xin lỗi bạn",
                    $"Xin lỗi bạn, ca dọn nhà của bạn không thể bắt đầu vì đối tác chưa đủ người làm đúng giờ. Đơn đã được huỷ và hoàn 100% ({amount} đ). TỔ ẤM chân thành xin lỗi vì sự bất tiện này.",
                    new Dictionary<string, string> { ["orderId"] = order.OrderId.ToString(CultureInfo.InvariantCulture) }),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not send the apology of order {OrderId}.", order.OrderId);
        }

        return true;
    }
}
