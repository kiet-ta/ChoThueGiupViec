namespace CommonService.Domain.Enums;

/// <summary>JOB_ORDER.order_status. Initial value = first member. PENDING_PAYMENT, PAID, DISPATCHING (drawio MF1), ASSIGNED (PRD 4.1), COMPLETED, CANCELLED (BR-03, BR-10, Q04, Q15).</summary>
public enum JobOrderStatus
{
    PendingPayment,
    Paid,
    Dispatching,
    Assigned,
    Completed,
    Cancelled
}
