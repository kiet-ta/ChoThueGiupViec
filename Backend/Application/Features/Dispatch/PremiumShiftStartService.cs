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

    /// <summary>The customer, the amount and the status of an order (for the apology and to know whether the cancel happened); null when it does not exist.</summary>
    Task<PremiumOrderInfo?> GetOrderInfoAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The free cancel of a rescue assignment (PREM-RESCUE-01): <c>ASSIGNED -&gt; CANCELLED</c> (only while it is the worker's and still ASSIGNED) and the
    /// slot back to AVAILABLE. <c>CANCELLED</c>, not <c>CANCELLED_BY_WORKER</c>: it does not count towards the lock of Q15. False when it was no
    /// longer ASSIGNED.
    /// </summary>
    Task<bool> TryCancelRescueAsync(long assignmentId, int workerId, DateTime atUtc, CancellationToken cancellationToken = default);
}

/// <summary>What the apology of a cancelled Premium order needs.</summary>
public record PremiumOrderInfo(int CustomerId, decimal TotalAmount, JobOrderStatus Status);

/// <summary>
/// Decision D38 (PREM-START-01): a Premium order whose seats are not all present at the shift start + grace is cancelled: the agencies of the
/// absent seats are penalised first (-30 SLA points per order and the seat price per absent worker from the escrow; the penalty service is
/// idempotent, so a failure is retried on the next tick), then <see cref="AssignmentFailed"/> cancels the order, refunds 100 % and cancels every
/// assignment (the workers go back to their agency), and the customer gets an in-app apology.
/// </summary>
public class PremiumShiftStartService(
    IPremiumStartRepository repository,
    ISlaPenaltyService slaPenalties,
    PremiumOrderCanceller canceller,
    IClock clock,
    IOptions<BusinessRules> rules,
    IWorkerCancelRepository? workerCancels = null,
    ILogger<PremiumShiftStartService>? logger = null)
{
    public const string ApologyTopic = PremiumOrderCanceller.ApologyTopic;

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

        // A Super-Freelancer who took over a seat and did not come: ONE violation (a CANCELLED_BY_WORKER row, the count of Q15 / D35: three in
        // 30 days lock the account), no money. The cancel also turns their auto-accept switch off and moves the order to DISPATCHING, which
        // AssignmentFailed below still cancels.
        foreach (var freelancer in absent.Where(s => s.AgencyId is null))
        {
            if (workerCancels is null)
            {
                _logger.LogWarning("Order {OrderId}: the rescue worker {WorkerId} of seat {Seq} was not present in time.", order.OrderId, freelancer.WorkerId, freelancer.Seq);
                continue;
            }

            var cancel = rules.Value.Cancel;
            await workerCancels.TryCancelAsync(
                freelancer.AssignmentId, freelancer.WorkerId, now, now.AddDays(-Math.Max(1, cancel.WorkerCancelWindowDays)),
                Math.Max(1, cancel.WorkerCancelLockCount), cancellationToken);
        }

        // 2. Cancel the order (refund 100 %, release the hold, cancel every assignment) and apologise.
        return await canceller.CancelAsync(order.OrderId, reason, now, cancellationToken);
    }
}
