using System.Globalization;
using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Ledger;

/// <summary>The query values of a history or an export, checked and turned into UTC bounds: <c>from</c>/<c>to</c> are Vietnam days (<c>yyyy-MM-dd</c>, both included).</summary>
public record ParsedLedgerFilter(DateTime? FromUtc, DateTime? ToExclusiveUtc, LedgerEntryType? Type, int Page, int PageSize);

/// <summary>Reads <see cref="LedgerHistoryRequest"/> values; every mistake is collected under its field name and thrown as a 400.</summary>
public class LedgerFilterParser(IClock clock)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>The filter of the request; adds the field errors to <paramref name="errors"/> (the caller throws when there are any).</summary>
    public ParsedLedgerFilter Parse(LedgerHistoryRequest request, Dictionary<string, string[]> errors)
    {
        var from = ParseDay(request.From, "from", errors);
        var to = ParseDay(request.To, "to", errors);
        if (from is not null && to is not null && from > to) errors["from"] = ["from must not be after to."];

        LedgerEntryType? type = null;
        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            type = ParseEnum<LedgerEntryType>(request.Type);
            if (type is null) errors["type"] = [$"type must be one of: {string.Join(", ", Names<LedgerEntryType>())}."];
        }

        if (request.Page is < 1) errors["page"] = ["page must be at least 1."];
        if (request.PageSize is < 1 or > MaxPageSize) errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];

        return new ParsedLedgerFilter(
            from is { } f ? LocalDayStartUtc(f) : null,
            to is { } t ? LocalDayStartUtc(t.AddDays(1)) : null, // the whole last day is included
            type, request.Page ?? 1, request.PageSize ?? DefaultPageSize);
    }

    /// <summary>The Admin's account filter: <c>partyType</c> alone is every account of the type, with <c>partyId</c> one account.</summary>
    public static LedgerPartyFilter? ParseParty(LedgerHistoryRequest request, Dictionary<string, string[]> errors)
    {
        if (!string.IsNullOrWhiteSpace(request.PartyType))
        {
            if (ParseEnum<LedgerPartyType>(request.PartyType) is { } type)
            {
                // The platform's own accounts have no id: the type is the account. For the others, a type without an id means every account of it.
                var platform = type is LedgerPartyType.PlatformClearing or LedgerPartyType.PlatformRevenue;
                return new LedgerPartyFilter(type, platform ? null : request.PartyId, !platform && request.PartyId is null);
            }

            errors["partyType"] = [$"partyType must be one of: {string.Join(", ", Names<LedgerPartyType>())}."];
        }
        else if (request.PartyId is not null)
        {
            errors["partyId"] = ["partyId needs a partyType."];
        }

        return null;
    }

    public DateTime LocalDayStartUtc(DateOnly day) =>
        DateTime.SpecifyKind(clock.ToUtc(new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Unspecified)), DateTimeKind.Utc);

    private static DateOnly? ParseDay(string? text, string field, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return day;
        errors[field] = [$"{field} must be a date in the form yyyy-MM-dd."];
        return null;
    }

    private static TEnum? ParseEnum<TEnum>(string text) where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Cast<TEnum?>().FirstOrDefault(v => string.Equals(DbEnum.ToDb(v!.Value), text.Trim(), StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Names<TEnum>() where TEnum : struct, Enum => Enum.GetValues<TEnum>().Select(v => DbEnum.ToDb(v));
}
