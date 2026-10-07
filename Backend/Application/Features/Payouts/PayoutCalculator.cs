using CommonService.Domain.Enums;
using CommonService.Domain.ValueObjects;

namespace CommonService.Application.Features.Payouts;

/// <summary>Who is paid: a freelancer worker or an agency (<c>PAYOUT_ITEM.payee_type</c>).</summary>
public readonly record struct PayeeKey(PayeeType Type, int Id);

/// <summary>An assignment that may be paid out, as read from the flat <c>JOB_ASSIGNMENT</c> node.</summary>
/// <param name="AbsenceFeeAmount">The approved customer-absence fee (decision Q10); only meaningful for <c>ABSENT</c>.</param>
public sealed record PayoutAssignmentRow(
    long AssignmentId, int WorkerId, int? AgencyId, JobAssignmentStatus Status, decimal GrossAmount, decimal CommissionRate, decimal? AbsenceFeeAmount);

/// <summary>One payout item computed for a payee.</summary>
public sealed record PayoutLine(
    PayeeKey Payee, int JobCount, decimal Gross, decimal Commission, decimal Penalty, decimal Net, IReadOnlyList<long> AssignmentIds);

/// <summary>The money of one assignment: what is paid and what the platform keeps.</summary>
public readonly record struct PayoutAmounts(decimal Gross, decimal Commission)
{
    public decimal Payable => Gross - Commission;
}

/// <summary>
/// The pure arithmetic of the monthly payout (contract payouts.md 2.1; decisions Q10, Q11, G-2). Nothing here reads a database, so the
/// same function serves the batch and the worker's income screen.
/// </summary>
public static class PayoutCalculator
{
    /// <summary>
    /// A COMPLETED assignment pays <c>gross</c> less the commission frozen at its creation, rounded per assignment (G-2). An approved
    /// absence fee (ABSENT) is paid in full with no commission: "the platform keeps nothing from this fee" (Q10). Any other state pays nothing.
    /// </summary>
    public static PayoutAmounts AmountsOf(PayoutAssignmentRow row) => row.Status switch
    {
        JobAssignmentStatus.Completed => new PayoutAmounts(row.GrossAmount, Vnd.Commission(row.GrossAmount, row.CommissionRate)),
        JobAssignmentStatus.Absent when row.AbsenceFeeAmount is > 0 => new PayoutAmounts(row.AbsenceFeeAmount.Value, 0m),
        _ => new PayoutAmounts(0m, 0m),
    };

    public static PayeeKey PayeeOf(PayoutAssignmentRow row) =>
        row.AgencyId is { } agency ? new PayeeKey(PayeeType.Agency, agency) : new PayeeKey(PayeeType.Freelancer, row.WorkerId);

    /// <summary>
    /// One line per payee. <paramref name="penaltiesDecided"/> is everything decided against each payee up to the end of the month and
    /// <paramref name="penaltiesApplied"/> what earlier CLOSED batches already took; the difference is pending, capped by what the payee
    /// earns this month so the net never goes below 0 (question P3), and the rest simply stays pending for the next batch.
    /// </summary>
    public static IReadOnlyList<PayoutLine> Build(
        IEnumerable<PayoutAssignmentRow> rows,
        IReadOnlyDictionary<PayeeKey, decimal> penaltiesDecided,
        IReadOnlyDictionary<PayeeKey, decimal> penaltiesApplied)
    {
        var lines = new List<PayoutLine>();
        var groups = rows
            .Select(r => (Row: r, Amounts: AmountsOf(r)))
            .Where(x => x.Amounts.Gross > 0)
            .GroupBy(x => PayeeOf(x.Row))
            .OrderBy(g => g.Key.Type).ThenBy(g => g.Key.Id);

        foreach (var group in groups)
        {
            var gross = group.Sum(x => x.Amounts.Gross);
            var commission = group.Sum(x => x.Amounts.Commission);
            var pending = Math.Max(0m, penaltiesDecided.GetValueOrDefault(group.Key) - penaltiesApplied.GetValueOrDefault(group.Key));
            var penalty = Math.Min(pending, gross - commission);
            lines.Add(new PayoutLine(
                group.Key, group.Count(), gross, commission, penalty, gross - commission - penalty,
                group.Select(x => x.Row.AssignmentId).OrderBy(id => id).ToList()));
        }

        return lines;
    }

    /// <summary>
    /// Splits <paramref name="amount"/> by <paramref name="weights"/> in whole VND, the remainder on the last part so the parts add up
    /// exactly (decision G-2); a part never goes below 0.
    /// </summary>
    public static IReadOnlyList<decimal> SplitByWeight(decimal amount, IReadOnlyList<decimal> weights)
    {
        var parts = new decimal[weights.Count];
        if (amount <= 0 || weights.Count == 0) return parts;

        var total = weights.Sum();
        var given = 0m;
        for (var i = 0; i < weights.Count; i++)
        {
            parts[i] = i == weights.Count - 1
                ? amount - given
                : total > 0 ? Math.Min(Vnd.Round(amount * weights[i] / total), amount - given) : 0m;
            given += parts[i];
        }

        return parts;
    }
}
