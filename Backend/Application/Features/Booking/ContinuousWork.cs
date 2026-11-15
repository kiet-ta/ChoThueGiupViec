using CommonService.Application.Common.Options;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Booking;

/// <summary>
/// Decision D35: an ECONOMY order of a large house (area over <c>Area.StandardMaxM2</c>) is done by ONE worker, who may need a
/// 2nd consecutive job (the next shift of the same day). The order stores nothing extra for this: a large ECONOMY order with a
/// single required worker is "continuous work"; older large ECONOMY orders have <c>required_workers = 2</c> (BR-02) and stay as they were.
/// </summary>
public static class ContinuousWork
{
    public static bool Is(JobOrder order, BusinessRules rules)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(rules);
        return order.ServiceTier == ServiceTier.Economy
            && order.RequiredWorkers == 1
            && order.AreaSnapshotM2 > rules.Area.StandardMaxM2;
    }

    /// <summary>The shift that follows <paramref name="shiftCode"/> on the same day (the 2nd job of a continuous order); null after the evening shift or for an unknown code.</summary>
    public static string? NextShift(string? shiftCode) => shiftCode switch
    {
        BookingShifts.Morning => BookingShifts.Afternoon,
        BookingShifts.Afternoon => BookingShifts.Evening,
        _ => null,
    };
}
