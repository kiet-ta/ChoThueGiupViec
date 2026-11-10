using CommonService.Application.Features.Payouts;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Tests.Payouts;

/// <summary>The pure arithmetic of the monthly settlement from the entries of one payee (decision D28).</summary>
public class PayoutSettlementCalculatorTests
{
    private static readonly DateTime From = new(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc);

    private static LedgerEntry Row(LedgerEntryType type, bool toPayee, decimal amount, DateTime at, LedgerRefType refType = LedgerRefType.Assignment, long refId = 1) => new()
    {
        EntryType = type,
        Amount = amount,
        CreatedAt = at,
        RefType = refType,
        RefId = refId,
        FromPartyType = toPayee ? LedgerPartyType.PlatformClearing : LedgerPartyType.Worker,
        FromPartyId = toPayee ? null : 7,
        ToPartyType = toPayee ? LedgerPartyType.Worker : LedgerPartyType.External,
        ToPartyId = toPayee ? 7 : null,
    };

    private static SettlementFigures Compute(IReadOnlyList<LedgerEntry> entries, IReadOnlyList<LedgerEntry>? commissions = null, decimal held = 0m) =>
        PayoutSettlementCalculator.Compute(LedgerPartyType.Worker, 7, entries, commissions ?? [], From, To, held);

    [Fact]
    public void A_payee_without_entries_and_with_a_zero_balance_has_no_activity()
    {
        var figures = Compute([]);

        Assert.False(figures.HasActivity);
        Assert.Equal((0m, 0m, 0), (figures.Net, figures.Gross, figures.JobCount));
    }

    [Fact]
    public void A_balance_without_entries_in_the_month_is_still_activity_so_the_debt_or_credit_is_listed()
    {
        var older = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        var debt = Compute([Row(LedgerEntryType.Penalty, false, 40_000m, older)]);
        var credit = Compute([Row(LedgerEntryType.JobEarning, true, 40_000m, older)]);

        Assert.Equal((true, -40_000m, 0m), (debt.HasActivity, debt.CarryIn, debt.Net));
        Assert.Equal((true, 40_000m, 40_000m), (credit.HasActivity, credit.CarryIn, credit.Net));
    }

    [Fact]
    public void Entries_after_the_cut_off_and_commissions_of_other_sources_are_ignored()
    {
        var inMonth = new DateTime(2026, 10, 10, 3, 0, 0, DateTimeKind.Utc);
        var figures = Compute(
            [Row(LedgerEntryType.JobEarning, true, 80_000m, inMonth, refId: 1), Row(LedgerEntryType.JobEarning, true, 999m, To, refId: 2)],
            [Row(LedgerEntryType.Commission, true, 20_000m, inMonth, refId: 1), Row(LedgerEntryType.Commission, true, 5m, inMonth, refId: 3)]);

        Assert.Equal((100_000m, 20_000m, 80_000m, 1), (figures.Gross, figures.Commission, figures.Net, figures.JobCount));
        Assert.Equal(0m, figures.Adjustments);
    }

    [Fact]
    public void The_held_amount_is_kept_and_a_negative_balance_is_never_a_payment()
    {
        var inMonth = new DateTime(2026, 10, 10, 3, 0, 0, DateTimeKind.Utc);
        var earned = Compute([Row(LedgerEntryType.JobEarning, true, 288_000m, inMonth)], held: 100_000m);
        var owed = Compute([Row(LedgerEntryType.Penalty, false, 10_000m, inMonth)], held: 5_000m);

        Assert.Equal((188_000m, 288_000m), (earned.Net, earned.BalanceAtCutoff));
        Assert.Equal((0m, -10_000m), (owed.Net, owed.BalanceAtCutoff));
    }

    [Fact]
    public void An_extension_and_an_absence_fee_count_but_only_the_extension_has_a_commission()
    {
        var inMonth = new DateTime(2026, 10, 10, 3, 0, 0, DateTimeKind.Utc);
        var figures = Compute(
            [
                Row(LedgerEntryType.ExtensionEarning, true, 80_000m, inMonth, LedgerRefType.Extension, 5),
                Row(LedgerEntryType.AbsenceFee, true, 104_000m, inMonth, LedgerRefType.Assignment, 9),
            ],
            [Row(LedgerEntryType.Commission, true, 20_000m, inMonth, LedgerRefType.Extension, 5)]);

        Assert.Equal((204_000m, 20_000m, 184_000m), (figures.Gross, figures.Commission, figures.Net));
        Assert.Equal([9L], figures.AssignmentIds.ToArray()); // the extension is not an assignment
    }
}
