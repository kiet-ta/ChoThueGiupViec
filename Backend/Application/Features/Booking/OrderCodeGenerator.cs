namespace CommonService.Application.Features.Booking;

/// <summary>
/// JOB_ORDER.order_code (VARCHAR(20), UNIQUE), contract booking.md B7: "GV" + yyMMdd (local date) + 6 random uppercase
/// alphanumerics, 14 characters, e.g. GV261015K3P9QZ. The caller retries with a new code on a UNIQUE clash.
/// </summary>
public static class OrderCodeGenerator
{
    public const string Prefix = "GV";
    public const int RandomLength = 6;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <param name="localDate">Today's date in Asia/Ho_Chi_Minh (IClock.LocalToday).</param>
    /// <param name="nextIndex">Returns a random integer in [0, maxExclusive); injected so tests are deterministic.</param>
    public static string Create(DateOnly localDate, Func<int, int> nextIndex)
    {
        ArgumentNullException.ThrowIfNull(nextIndex);
        var suffix = new char[RandomLength];
        for (var i = 0; i < RandomLength; i++)
        {
            suffix[i] = Alphabet[nextIndex(Alphabet.Length)];
        }

        return $"{Prefix}{localDate:yyMMdd}{new string(suffix)}";
    }
}
