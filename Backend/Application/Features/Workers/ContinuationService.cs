using CommonService.Application.Common.Options;
using CommonService.Application.Features.Booking;
using CommonService.Application.Interfaces.Ports;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Workers;

/// <summary>The 2nd-job state of an order (decision D35).</summary>
/// <param name="OpenedAt">When the 2nd job opened; null while it has not.</param>
public record ContinuationState(bool IsContinuousWork, DateTime? OpenedAt, DateOnly ScheduledDate, string ShiftCode);

/// <summary>A continuous order whose only job is running and whose 2nd job has not opened (candidate of the 15-minute sweep).</summary>
public record DueContinuation(long OrderId, DateOnly ScheduledDate, string ShiftCode);

/// <summary>
/// Storage of the 2nd job of a large ECONOMY house (decision D35). Lives in Workers: the 2nd job is opened by the worker not confirming in time
/// or by the customer's "not clean" at acceptance.
/// </summary>
public interface IContinuationRepository
{
    Task<ContinuationState?> GetStateAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>Marks the 2nd job open only while the order is continuous and was not opened before; false otherwise.</summary>
    Task<bool> TryOpenAsync(long orderId, DateTime atUtc, CancellationToken cancellationToken = default);

    /// <summary>Continuous orders with no 2nd job yet and an assignment <c>IN_PROGRESS</c>.</summary>
    Task<IReadOnlyList<DueContinuation>> GetOpenCandidatesAsync(CancellationToken cancellationToken = default);
}

public interface IContinuationService
{
    /// <summary>Opens the 2nd job of every continuous order whose shift ended more than <c>Dispatch.ContinuationConfirmMinutes</c> ago with the worker not done. Returns how many opened.</summary>
    Task<int> OpenDueAsync(CancellationToken cancellationToken = default);
}

public class ContinuationService(
    IContinuationRepository continuations,
    IClock clock,
    IOptions<BusinessRules>? rules = null) : IContinuationService
{
    private readonly int _confirmMinutes = rules?.Value.Dispatch.ContinuationConfirmMinutes ?? new DispatchRules().ContinuationConfirmMinutes;

    public async Task<int> OpenDueAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var opened = 0;
        foreach (var candidate in await continuations.GetOpenCandidatesAsync(cancellationToken))
        {
            if (BookingShifts.GetLocalTimes(candidate.ShiftCode) is not { } times)
            {
                continue;
            }

            var shiftEndUtc = clock.ToUtc(DateTime.SpecifyKind(candidate.ScheduledDate.ToDateTime(times.End), DateTimeKind.Unspecified));
            if (now < shiftEndUtc.AddMinutes(_confirmMinutes))
            {
                continue;
            }

            if (await continuations.TryOpenAsync(candidate.OrderId, now, cancellationToken))
            {
                opened++;
            }
        }

        return opened;
    }
}
