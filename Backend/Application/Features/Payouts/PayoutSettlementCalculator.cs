using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Payouts;

/// <summary>
/// The figures of one payee's monthly settlement, all taken from the ledger (decision D28, contract ledger.md section 7).
/// <see cref="Adjustments"/> is whatever else moved the balance in the month (reversals); it is 0 when the month has only the
/// ordinary entries, so <c>BalanceAtCutoff = CarryIn + Gross - Commission - Penalty - Advance + Adjustments</c> always holds.
/// </summary>
public record SettlementFigures(
    int JobCount,
    decimal Gross,
    decimal Commission,
    decimal Penalty,
    decimal Advance,
    decimal CarryIn,
    decimal Adjustments,
    decimal BalanceAtCutoff,
    decimal Held,
    decimal Net,
    bool HasActivity,
    IReadOnlyList<long> AssignmentIds);

/// <summary>
/// The pure arithmetic of the monthly settlement. Nothing here reads a database: the entries of the payee created before the cut-off are
/// passed in, so the same function serves the batch and its tests. The net is <c>max(0, balance at the cut-off - held)</c>: a negative
/// balance (a penalty larger than the month) is carried forward and never becomes a negative payment, and the amount held by a
/// pending withdrawal request stays in the wallet.
/// </summary>
public static class PayoutSettlementCalculator
{
    /// <param name="entriesBeforeCutoff">Every entry of the payee created before <paramref name="toUtc"/> (as sender or receiver).</param>
    /// <param name="commissions">The COMMISSION entries of the payee's earnings (found by the same reference as the earning).</param>
    /// <param name="held">The amount of the worker's pending withdrawal request (0 for an agency).</param>
    public static SettlementFigures Compute(
        LedgerPartyType type, int id, IReadOnlyList<LedgerEntry> entriesBeforeCutoff, IReadOnlyList<LedgerEntry> commissions,
        DateTime fromUtc, DateTime toUtc, decimal held)
    {
        bool Received(LedgerEntry e) => e.ToPartyType == type && e.ToPartyId == id;
        bool Sent(LedgerEntry e) => e.FromPartyType == type && e.FromPartyId == id;
        decimal Signed(LedgerEntry e) => Received(e) ? e.Amount : Sent(e) ? -e.Amount : 0m;

        var before = entriesBeforeCutoff.Where(e => e.CreatedAt < fromUtc).ToList();
        var month = entriesBeforeCutoff.Where(e => e.CreatedAt >= fromUtc && e.CreatedAt < toUtc).ToList();

        var carryIn = before.Sum(Signed);
        var balance = entriesBeforeCutoff.Where(e => e.CreatedAt < toUtc).Sum(Signed);

        var earned = month.Where(e => Received(e) && e.EntryType is LedgerEntryType.JobEarning or LedgerEntryType.ExtensionEarning).ToList();
        var absence = month.Where(e => Received(e) && e.EntryType == LedgerEntryType.AbsenceFee).ToList();

        // The platform's commission of an earning is the COMMISSION entry with the same source (assignment or extension).
        var sources = earned.Select(e => (e.RefType, e.RefId)).ToHashSet();
        var commission = commissions.Where(c => sources.Contains((c.RefType, c.RefId))).Sum(c => c.Amount);

        var gross = earned.Sum(e => e.Amount) + commission + absence.Sum(e => e.Amount);
        var penalty = month.Where(e => Sent(e) && e.EntryType == LedgerEntryType.Penalty).Sum(e => e.Amount);
        var advance = month.Where(e => Sent(e) && e.EntryType == LedgerEntryType.Withdrawal).Sum(e => e.Amount);
        var adjustments = balance - (carryIn + gross - commission - penalty - advance);

        var assignmentIds = earned.Where(e => e.RefType == LedgerRefType.Assignment).Select(e => e.RefId)
            .Concat(absence.Where(e => e.RefType == LedgerRefType.Assignment).Select(e => e.RefId))
            .Distinct().OrderBy(x => x).ToList();

        return new SettlementFigures(
            assignmentIds.Count, gross, commission, penalty, advance, carryIn, adjustments, balance, held,
            Math.Max(0m, balance - held), month.Count > 0 || balance != 0m, assignmentIds);
    }
}
