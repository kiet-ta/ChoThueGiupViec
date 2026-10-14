namespace CommonService.Domain.Enums;

/// <summary>JOB_ORDER.order_status. Initial value = first member. PENDING_PAYMENT, PAID, DISPATCHING (drawio MF1), ASSIGNED (PRD 4.1), COMPLETED, CANCELLED (BR-03, BR-10, Q04, Q15), AWAITING_FALLBACK (BR-02 fallback, #310).</summary>
public enum JobOrderStatus
{
    PendingPayment,
    Paid,
    Dispatching,
    Assigned,
    Completed,
    Cancelled,

    /// <summary>
    /// BR-02 fallback: a 2-seat order has one live seat and no candidate for the other. It waits for the customer to agree to one worker
    /// doing two consecutive shifts (then for that worker to confirm the second shift) or to decline (cancel + 100 % refund). Appended
    /// last so no stored or ordinal value moves; stored as AWAITING_FALLBACK (varchar(20), no CHECK, no migration).
    /// </summary>
    AwaitingFallback
}
