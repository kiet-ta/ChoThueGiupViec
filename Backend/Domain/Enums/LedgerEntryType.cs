namespace CommonService.Domain.Enums;

public enum LedgerEntryType
{
    OrderPaid, ExtensionPaid, JobEarning, ExtensionEarning, Commission, Refund,
    AbsenceFee, Penalty, Subscription, EscrowDeposit, EscrowPenalty, EscrowReversal,
    Withdrawal, Settlement, Reversal
}
