namespace CommonService.Application.Features.Booking;

/// <summary>
/// Area brackets of decisions Q01 (the values of PRICE_RULE.area_bracket, seeded by BASE-10).
/// The limits are part of the bracket names, so they are fixed here and not configuration: a different limit would be a new bracket.
/// </summary>
public static class AreaBrackets
{
    public const string UpTo30 = "UP_TO_30";
    public const string From31To80 = "FROM_31_TO_80";
    public const string Over80 = "OVER_80";

    private const decimal UpTo30MaxM2 = 30m;
    private const decimal From31To80MaxM2 = 80m;

    /// <param name="totalAreaM2">The address S_total (PRD §1.1), strictly positive.</param>
    public static string Classify(decimal totalAreaM2)
    {
        if (totalAreaM2 <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalAreaM2), totalAreaM2, "Area must be strictly positive.");
        }

        return totalAreaM2 <= UpTo30MaxM2 ? UpTo30
            : totalAreaM2 <= From31To80MaxM2 ? From31To80
            : Over80;
    }
}
