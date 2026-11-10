using CommonService.Application.Features.Payouts;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.ValueObjects;

namespace CommonService.Application.Features.Ledger;

/// <summary>Idempotency keys of the ledger entries (contract ledger.md section 8). One place so the events and the sweep agree.</summary>
public static class LedgerKeys
{
    public const string RevenueSuffix = ":REVENUE";

    public static string OrderPaid(long orderId) => $"ORDER_PAID:ORDER:{orderId}";
    public static string ExtensionPaid(int extensionId) => $"EXTENSION_PAID:EXTENSION:{extensionId}";
    public static string JobEarning(long assignmentId) => $"JOB_EARNING:ASSIGNMENT:{assignmentId}";
    public static string AssignmentCommission(long assignmentId) => $"COMMISSION:ASSIGNMENT:{assignmentId}";
    public static string ExtensionEarning(int extensionId) => $"EXTENSION_EARNING:EXTENSION:{extensionId}";
    public static string ExtensionCommission(int extensionId) => $"COMMISSION:EXTENSION:{extensionId}";
    public static string Refund(long orderId, DateTime refundedAtUtc) => $"REFUND:ORDER:{orderId}:{refundedAtUtc.Ticks}";
    public static string RefundTarget(long orderId, decimal target) => $"REFUND:ORDER:{orderId}:TO:{target:0}";
    public static string AbsenceFee(long assignmentId) => $"ABSENCE_FEE:ASSIGNMENT:{assignmentId}";
    public static string Penalty(int disputeId, long assignmentId) => $"PENALTY:DISPUTE:{disputeId}:ASSIGNMENT:{assignmentId}";
    public static string Subscription(long paymentId) => $"SUBSCRIPTION:PAYMENT:{paymentId}";
    public static string Escrow(long escrowTxnId) => $"ESCROW:{escrowTxnId}";
    public static string Withdrawal(int withdrawalId) => $"WITHDRAWAL:{withdrawalId}";
    public static string Settlement(string periodMonth, LedgerPartyType type, int partyId) => $"SETTLEMENT:{periodMonth}:{DbEnum.ToDb(type)}:{partyId}";
}

/// <summary>
/// The pure rules that turn a source fact into ledger entries (decision D28, contract ledger.md section 3). No I/O, so the event
/// handlers, the reconciliation sweep and the tests all build the same entries with the same keys.
/// </summary>
public static class LedgerRules
{
    /// <summary>Earnings of a job are withdrawable this many hours after completion (decision A: the dispute window).</summary>
    public const int WithdrawalHoldHours = 24;

    /// <summary>DISPUTE_TICKET.category of an absence-fee dispute: the only Agency fault that is a penalty (others go through escrow, D16).</summary>
    public const string AbsentFeeCategory = "ABSENT_FEE";

    public static readonly LedgerParty Clearing = new(LedgerPartyType.PlatformClearing, null);
    public static readonly LedgerParty Revenue = new(LedgerPartyType.PlatformRevenue, null);

    public static LedgerParty Customer(int customerId) => new(LedgerPartyType.External, customerId);

    /// <summary>An agency assignment pays the agency, never the worker; otherwise the freelancer is paid (rule 3).</summary>
    public static LedgerParty Payee(LedgerAssignmentSource assignment) =>
        assignment.AgencyId is { } agencyId ? new LedgerParty(LedgerPartyType.Agency, agencyId) : new LedgerParty(LedgerPartyType.Worker, assignment.WorkerId);

    /// <summary>
    /// The whole-VND value of the seat an assignment fills (rule 5): the order total split equally over the seats with
    /// <see cref="PayoutCalculator.SplitByWeight"/> and taken by <c>assignment_seq</c>. A replacement keeps the seq of the seat it
    /// replaces. Outside the seats the stored gross is rounded, because the stored value can be fractional.
    /// </summary>
    public static decimal SeatGross(LedgerOrderSource order, LedgerAssignmentSource assignment)
    {
        var seats = Math.Max(1, order.RequiredWorkers);
        if (assignment.Seat < 1 || assignment.Seat > seats) return Vnd.Round(assignment.GrossAmount);
        var shares = PayoutCalculator.SplitByWeight(Vnd.Round(order.TotalAmount), Enumerable.Repeat(1m, seats).ToList());
        return shares[assignment.Seat - 1];
    }

    /// <summary>ORDER_PAID (extensionId null) or EXTENSION_PAID: the customer's money enters the clearing account.</summary>
    public static NewLedgerEntry CustomerPayment(int customerId, decimal amount, long orderId, int? extensionId) =>
        extensionId is null
            ? new NewLedgerEntry(Customer(customerId), Clearing, amount, LedgerEntryType.OrderPaid, LedgerRefType.Order, orderId, orderId, null,
                LedgerKeys.OrderPaid(orderId))
            : new NewLedgerEntry(Customer(customerId), Clearing, amount, LedgerEntryType.ExtensionPaid, LedgerRefType.Extension, extensionId.Value, orderId, null,
                LedgerKeys.ExtensionPaid(extensionId.Value));

    /// <summary>JOB_EARNING to the payee and COMMISSION to the platform; the two add up to the seat value (rule 2).</summary>
    public static IReadOnlyList<NewLedgerEntry> JobEntries(LedgerOrderSource order, LedgerAssignmentSource assignment, DateTime completedAtUtc)
    {
        var gross = SeatGross(order, assignment);
        var (earning, commission) = Split(gross, assignment.CommissionRate);
        var entries = new List<NewLedgerEntry>();
        if (earning > 0)
        {
            entries.Add(new NewLedgerEntry(Clearing, Payee(assignment), earning, LedgerEntryType.JobEarning, LedgerRefType.Assignment,
                assignment.AssignmentId, assignment.OrderId, AvailableAt(assignment, completedAtUtc), LedgerKeys.JobEarning(assignment.AssignmentId)));
        }

        if (commission > 0)
        {
            entries.Add(new NewLedgerEntry(Clearing, Revenue, commission, LedgerEntryType.Commission, LedgerRefType.Assignment,
                assignment.AssignmentId, assignment.OrderId, null, LedgerKeys.AssignmentCommission(assignment.AssignmentId)));
        }

        return entries;
    }

    /// <summary>The paid extension is split like a shift by the rate of the extension worker's assignment (decision I).</summary>
    public static IReadOnlyList<NewLedgerEntry> ExtensionEntries(LedgerExtensionSource extension, LedgerAssignmentSource assignment, DateTime completedAtUtc)
    {
        var (earning, commission) = Split(Vnd.Round(extension.ExtraAmount), assignment.CommissionRate);
        var entries = new List<NewLedgerEntry>();
        if (earning > 0)
        {
            entries.Add(new NewLedgerEntry(Clearing, Payee(assignment), earning, LedgerEntryType.ExtensionEarning, LedgerRefType.Extension,
                extension.ExtensionId, extension.OrderId, AvailableAt(assignment, completedAtUtc), LedgerKeys.ExtensionEarning(extension.ExtensionId)));
        }

        if (commission > 0)
        {
            entries.Add(new NewLedgerEntry(Clearing, Revenue, commission, LedgerEntryType.Commission, LedgerRefType.Extension,
                extension.ExtensionId, extension.OrderId, null, LedgerKeys.ExtensionCommission(extension.ExtensionId)));
        }

        return entries;
    }

    /// <summary>The compensation of an approved absence (the 40 % share), paid at once; the 60 % refund comes from OrderRefunded.</summary>
    public static NewLedgerEntry? AbsenceFee(LedgerAssignmentSource assignment, decimal amount) =>
        amount <= 0
            ? null
            : new NewLedgerEntry(Clearing, Payee(assignment), amount, LedgerEntryType.AbsenceFee, LedgerRefType.Assignment,
                assignment.AssignmentId, assignment.OrderId, null, LedgerKeys.AbsenceFee(assignment.AssignmentId));

    /// <summary>
    /// A refund consumes the order's clearing balance first and takes any shortfall from the platform revenue (the order was already
    /// split, e.g. a dispute compensation). With two sources the revenue entry adds <see cref="LedgerKeys.RevenueSuffix"/> to the key.
    /// </summary>
    public static IReadOnlyList<NewLedgerEntry> RefundEntries(long orderId, int customerId, decimal amount, decimal clearingBalance, string key)
    {
        var entries = new List<NewLedgerEntry>();
        if (amount <= 0) return entries;
        var fromClearing = Math.Min(amount, Math.Max(0m, clearingBalance));
        var fromRevenue = amount - fromClearing;
        if (fromClearing > 0)
        {
            entries.Add(new NewLedgerEntry(Clearing, Customer(customerId), fromClearing, LedgerEntryType.Refund, LedgerRefType.Order, orderId, orderId, null, key));
        }

        if (fromRevenue > 0)
        {
            entries.Add(new NewLedgerEntry(Revenue, Customer(customerId), fromRevenue, LedgerEntryType.Refund, LedgerRefType.Order, orderId, orderId, null,
                fromClearing > 0 ? key + LedgerKeys.RevenueSuffix : key));
        }

        return entries;
    }

    /// <summary>
    /// The penalties of a verdict, one per blamed assignment, with the same conditions and split as <c>EfPayoutPenaltySource</c>: a
    /// resolved ticket with a positive compensation, FREELANCER fault (assignments without agency) or AGENCY fault of an ABSENT_FEE
    /// dispute (assignments with an agency); the amount is divided by gross with <see cref="PayoutCalculator.SplitByWeight"/> over the
    /// assignments ordered by id.
    /// </summary>
    public static IReadOnlyList<(long AssignmentId, NewLedgerEntry Entry)> PenaltyEntries(
        LedgerDisputeSource dispute, IReadOnlyList<LedgerAssignmentSource> assignments)
    {
        var result = new List<(long, NewLedgerEntry)>();
        if (!dispute.IsResolved || dispute.CompensationAmount <= 0) return result;
        var freelancerFault = dispute.FaultParty == FaultParty.Freelancer;
        var agencyFault = dispute.FaultParty == FaultParty.Agency && dispute.Category == AbsentFeeCategory;
        if (!freelancerFault && !agencyFault) return result;

        var blamed = assignments
            .Where(a => a.OrderId == dispute.OrderId && (freelancerFault ? a.AgencyId is null : a.AgencyId is not null))
            .OrderBy(a => a.AssignmentId)
            .ToList();
        var shares = PayoutCalculator.SplitByWeight(dispute.CompensationAmount, blamed.Select(a => a.GrossAmount).ToList());
        for (var i = 0; i < blamed.Count; i++)
        {
            if (shares[i] <= 0) continue;
            result.Add((blamed[i].AssignmentId, new NewLedgerEntry(Payee(blamed[i]), Revenue, shares[i], LedgerEntryType.Penalty, LedgerRefType.Dispute,
                dispute.DisputeId, dispute.OrderId, null, LedgerKeys.Penalty(dispute.DisputeId, blamed[i].AssignmentId))));
        }

        return result;
    }

    /// <summary>The approved advance: the worker's virtual money leaves the platform (no real transfer, decision G-1).</summary>
    public static NewLedgerEntry Withdrawal(int workerId, decimal amount, int withdrawalId, int adminId) =>
        new(new LedgerParty(LedgerPartyType.Worker, workerId), new LedgerParty(LedgerPartyType.External, null), amount, LedgerEntryType.Withdrawal,
            LedgerRefType.Withdrawal, withdrawalId, null, null, LedgerKeys.Withdrawal(withdrawalId), null, adminId);

    /// <summary>The monthly settlement of a worker or agency: the net leaves the wallet for the outside (virtual money, decision G-1), referring to its payout item.</summary>
    public static NewLedgerEntry Settlement(LedgerPartyType type, int partyId, decimal net, int payoutItemId, string periodMonth) =>
        new(new LedgerParty(type, partyId), new LedgerParty(LedgerPartyType.External, null), net, LedgerEntryType.Settlement, LedgerRefType.PayoutItem,
            payoutItemId, null, null, LedgerKeys.Settlement(periodMonth, type, partyId));

    /// <summary>An agency's subscription payment is platform revenue.</summary>
    public static NewLedgerEntry? Subscription(int agencyId, decimal amount, long paymentId) =>
        amount <= 0
            ? null
            : new NewLedgerEntry(new LedgerParty(LedgerPartyType.External, agencyId), Revenue, amount, LedgerEntryType.Subscription, LedgerRefType.Payment,
                paymentId, null, null, LedgerKeys.Subscription(paymentId));

    /// <summary>
    /// Mirrors one ESCROW_TRANSACTION. A deposit comes from outside; a penalty is the agency's money passing to the platform revenue
    /// (the customer's refund is booked on its own from the order, rule 6); a reversal gives it back from the revenue.
    /// </summary>
    public static NewLedgerEntry? Escrow(LedgerEscrowSource txn)
    {
        if (txn.Amount <= 0) return null;
        var escrow = new LedgerParty(LedgerPartyType.AgencyEscrow, txn.AgencyId);
        var (from, to, type) = txn.Type switch
        {
            EscrowTransactionType.Deposit => (new LedgerParty(LedgerPartyType.External, txn.AgencyId), escrow, LedgerEntryType.EscrowDeposit),
            EscrowTransactionType.Penalty => (escrow, Revenue, LedgerEntryType.EscrowPenalty),
            _ => (Revenue, escrow, LedgerEntryType.EscrowReversal),
        };
        return new NewLedgerEntry(from, to, txn.Amount, type, LedgerRefType.EscrowTxn, txn.EscrowTxnId, null, null, LedgerKeys.Escrow(txn.EscrowTxnId));
    }

    private static DateTime? AvailableAt(LedgerAssignmentSource assignment, DateTime completedAtUtc) =>
        assignment.AgencyId is null ? completedAtUtc.AddHours(WithdrawalHoldHours) : null;

    private static (decimal Earning, decimal Commission) Split(decimal gross, decimal rate)
    {
        if (rate is < 0m or > 1m) throw new InvalidOperationException($"The commission rate {rate} is outside 0..1.");
        var commission = Vnd.Commission(gross, rate);
        return (gross - commission, commission);
    }
}
