using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Workers;

/// <summary>The seats of an order as the start-of-work gate sees them (decision D38, PREM-START-02).</summary>
/// <param name="IsPremium">PREMIUM order (agency workers).</param>
/// <param name="RequiredWorkers">How many seats the order has.</param>
/// <param name="LiveSeats">The live assignments (seat number and status); a seat with no live row is simply missing.</param>
public record OrderSeatPresence(bool IsPremium, int RequiredWorkers, IReadOnlyList<(byte Seq, JobAssignmentStatus Status)> LiveSeats);

/// <summary>The seats of an order, read by the photo upload.</summary>
public interface IOrderSeatQuery
{
    /// <summary>Null when the order does not exist.</summary>
    Task<OrderSeatPresence?> GetAsync(long orderId, CancellationToken cancellationToken = default);
}

/// <summary>Error codes of the start-of-work gate.</summary>
public static class StartGateErrorCodes
{
    public const string PartnerNotCheckedIn = "PARTNER_NOT_CHECKED_IN";
}

/// <summary>
/// Decision D38 (PREM-START-02): the work of a PREMIUM order starts only when every seat is present (its assignment is <c>CHECKED_IN</c> or
/// later). Pure rule, no database. ECONOMY orders and a PREMIUM order with one seat (its worker is present by the time they ask) are never held.
/// </summary>
public static class PremiumStartGate
{
    private static readonly HashSet<JobAssignmentStatus> Present =
        [JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress, JobAssignmentStatus.AwaitingAcceptance, JobAssignmentStatus.Completed];

    /// <summary>True when the work must wait for a colleague who has not checked in yet.</summary>
    public static bool MustWaitForPartner(OrderSeatPresence seats)
    {
        ArgumentNullException.ThrowIfNull(seats);
        if (!seats.IsPremium)
        {
            return false;
        }

        var present = seats.LiveSeats.Where(s => Present.Contains(s.Status)).Select(s => s.Seq).Distinct().Count();
        return present < seats.RequiredWorkers;
    }
}
