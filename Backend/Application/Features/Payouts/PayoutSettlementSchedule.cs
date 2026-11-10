using CommonService.Application.Interfaces.Ports;

namespace CommonService.Application.Features.Payouts;

/// <summary>
/// When the monthly settlement is due (decision B, contract ledger.md section 7): the previous month, once the Asia/Ho_Chi_Minh clock is
/// past 00:05 on the first day. Pure, so the schedule is tested with a pinned clock. Settling is idempotent, so the worker may ask every
/// hour: an already closed month answers at once.
/// </summary>
public static class PayoutSettlementSchedule
{
    /// <summary>How often the worker looks (the month is settled at most this late after 00:05 of the first day).</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    /// <summary>The first minutes of the month are left alone so every entry of the last minutes of the old month is in the ledger.</summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(5);

    /// <summary>The month to settle now (<c>YYYY-MM</c>), or null while the first five minutes of a month have not passed.</summary>
    public static string? DueMonth(IClock clock)
    {
        var local = clock.ToLocal(clock.UtcNow);
        var firstOfMonth = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        if (local < firstOfMonth + GracePeriod) return null;

        var previous = firstOfMonth.AddMonths(-1);
        return PayoutConstants.FormatPeriod(previous.Year, previous.Month);
    }
}
