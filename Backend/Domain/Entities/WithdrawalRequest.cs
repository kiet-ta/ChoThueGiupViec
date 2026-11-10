using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

public partial class WithdrawalRequest
{
    public int WithdrawalId { get; set; }
    public int WorkerId { get; set; }
    public decimal Amount { get; set; }
    public WithdrawalStatus RequestStatus { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankName { get; set; }
    public string? RejectReason { get; set; }
    public long? EntryId { get; set; }
    public int? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime RequestedAt { get; set; }
}
