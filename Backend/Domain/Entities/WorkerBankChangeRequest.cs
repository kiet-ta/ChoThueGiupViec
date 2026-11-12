using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <summary>
/// A freelancer's request to change the payout account (LEDGER-28): an Admin approves or rejects it. The previous account is kept so the
/// Admin sees both side by side. The sent time (<see cref="RequestedAt"/>) of the latest request starts the lock during which no new
/// request can be sent, whatever the Admin decided.
/// </summary>
public partial class WorkerBankChangeRequest
{
    public int RequestId { get; set; }
    public int WorkerId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string BankAccountNo { get; set; } = string.Empty;
    public string? PreviousBankName { get; set; }
    public string? PreviousBankAccountNo { get; set; }
    public BankChangeStatus RequestStatus { get; set; }
    public string? RejectReason { get; set; }
    public int? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime RequestedAt { get; set; }
}
