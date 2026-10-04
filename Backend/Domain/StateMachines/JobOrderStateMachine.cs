using CommonService.Domain.Enums;

namespace CommonService.Domain.StateMachines;

/// <summary>
/// JOB_ORDER life cycle. Sources: drawio MF1 (PENDING_PAYMENT -> PAID -> DISPATCHING), PRD 4.1 (ASSIGNED),
/// BR-03 (no worker within 10 km -> cancel + refund), BR-10 (incident -> re-dispatch or cancel), decisions Q04 (unpaid QR -> cancelled),
/// Q15 (customer cancel; worker cancel -> order is re-dispatched).
/// </summary>
public static class JobOrderStateMachine
{
    public static readonly StateMachine<JobOrderStatus> Instance = new("JobOrder",
    [
        (JobOrderStatus.PendingPayment, JobOrderStatus.Paid),
        (JobOrderStatus.PendingPayment, JobOrderStatus.Cancelled),
        (JobOrderStatus.Paid, JobOrderStatus.Dispatching),
        (JobOrderStatus.Paid, JobOrderStatus.Cancelled),
        (JobOrderStatus.Dispatching, JobOrderStatus.Assigned),
        (JobOrderStatus.Dispatching, JobOrderStatus.Cancelled),
        (JobOrderStatus.Assigned, JobOrderStatus.Dispatching),
        (JobOrderStatus.Assigned, JobOrderStatus.Completed),
        (JobOrderStatus.Assigned, JobOrderStatus.Cancelled),
    ]);
}
