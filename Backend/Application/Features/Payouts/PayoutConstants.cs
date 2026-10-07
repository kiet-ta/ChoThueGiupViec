using System.Globalization;
using System.Text.RegularExpressions;

namespace CommonService.Application.Features.Payouts;

/// <summary>Text values and the period parsing of the monthly payout (contract payouts.md, question P7). One place to change them.</summary>
public static partial class PayoutConstants
{
    public const string BatchDraft = "DRAFT";
    public const string BatchClosed = "CLOSED";
    public const string ItemPending = "PENDING";
    public const string ItemTransferred = "TRANSFERRED";

    public const string PayeeFreelancer = "FREELANCER";
    public const string PayeeAgency = "AGENCY";

    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    /// <summary>The audit row of a closed batch (decision G-5: a money decision is audited).</summary>
    public const string AuditEntityType = "PAYOUT_BATCH";

    public const string AuditFieldName = "batch_status";

    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex PeriodPattern();

    /// <summary>Parses <c>YYYY-MM</c> (the value of <c>PAYOUT_BATCH.period_month</c>); false for anything else.</summary>
    public static bool TryParsePeriod(string? text, out int year, out int month)
    {
        year = 0;
        month = 0;
        if (text is null || !PeriodPattern().IsMatch(text)) return false;
        var parsedYear = int.Parse(text.AsSpan(0, 4), CultureInfo.InvariantCulture);
        if (parsedYear < 2000) return false; // before the product existed

        year = parsedYear;
        month = int.Parse(text.AsSpan(5, 2), CultureInfo.InvariantCulture);
        return true;
    }

    public static string FormatPeriod(int year, int month) => string.Create(CultureInfo.InvariantCulture, $"{year:0000}-{month:00}");
}
