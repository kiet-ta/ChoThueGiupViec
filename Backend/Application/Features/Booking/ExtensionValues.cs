namespace CommonService.Application.Features.Booking;

/// <summary>JOB_ORDER_EXTENSION.ext_status (VARCHAR(15)): the values of decision Q24 / B5. The column is a plain string, so no schema change.</summary>
public static class ExtensionStatuses
{
    public const string PendingPayment = "PENDING_PAYMENT";
    public const string Paid = "PAID";
    public const string Accepted = "ACCEPTED";
    public const string Declined = "DECLINED";
    public const string Expired = "EXPIRED";
}

/// <summary>JOB_ORDER_EXTENSION.worker_decision (VARCHAR(10)): the values of decision Q24 / B5.</summary>
public static class WorkerDecisions
{
    public const string Pending = "PENDING";
    public const string Accepted = "ACCEPTED";
    public const string Declined = "DECLINED";
}
