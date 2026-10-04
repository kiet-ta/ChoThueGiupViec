using System.Text.RegularExpressions;

namespace CommonService.Domain.Enums;

/// <summary>
/// Database spelling of enum members: PascalCase member -> UPPER_SNAKE_CASE (CheckedIn -> CHECKED_IN),
/// the spelling used by the PRD and the physical schema. Persistence (EF value converters, BASE-07) uses this
/// so the domain stays free of persistence concerns.
/// </summary>
public static partial class DbEnum
{
    public static string ToDb<TEnum>(TEnum value) where TEnum : struct, Enum =>
        WordBoundary().Replace(value.ToString(), "_$1").ToUpperInvariant();

    public static TEnum Parse<TEnum>(string dbValue) where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(ToDb(candidate), dbValue, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        throw new ArgumentException($"'{dbValue}' is not a valid {typeof(TEnum).Name}.", nameof(dbValue));
    }

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex WordBoundary();
}
