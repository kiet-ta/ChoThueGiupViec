namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// <c>BOOKING_SLOT.slot_status</c> values Dispatch moves a slot through (decisions Q26 defaults; the column is <c>varchar(10)</c> with no
/// CHECK, so no migration). <c>AVAILABLE</c> and <c>BLOCKED</c> are the worker's own toggle (Workers, M4).
/// </summary>
public static class DispatchSlotStatuses
{
    /// <summary>Free: the worker can be offered a job for this shift.</summary>
    public const string Available = "AVAILABLE";

    /// <summary>Held by an open offer (or an agency hold): no second offer can use it.</summary>
    public const string Held = "HELD";

    /// <summary>An assignment owns the slot.</summary>
    public const string Booked = "BOOKED";
}
