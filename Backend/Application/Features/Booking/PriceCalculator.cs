using CommonService.Domain.Enums;
using CommonService.Domain.ValueObjects;

namespace CommonService.Application.Features.Booking;

/// <param name="UnitPrice">Price per worker per shift (decisions Q01).</param>
/// <param name="TotalAmount">UnitPrice x RequiredWorkers, whole VND (G-2). Frozen into JOB_ORDER.total_amount by order creation.</param>
public sealed record PriceQuote(
    ServiceTier ServiceTier,
    decimal TotalAreaM2,
    string AreaBracket,
    int RequiredWorkers,
    decimal UnitPrice,
    decimal TotalAmount);

/// <summary>Pure price rule of decisions Q01: total = unit price x required workers. No DB, I/O or clock.</summary>
public static class PriceCalculator
{
    private const int MinWorkers = 1;
    private const int MaxWorkers = 2;

    /// <param name="requiredWorkers">1 or 2 (PRD BR-01/BR-02, decided by the shift rule of BE-M2-01); anything else is rejected.</param>
    public static PriceQuote Calculate(ServiceTier serviceTier, decimal totalAreaM2, int requiredWorkers, decimal unitPrice)
    {
        if (requiredWorkers is < MinWorkers or > MaxWorkers)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredWorkers), requiredWorkers, "Required workers must be 1 or 2 (BR-01/BR-02).");
        }

        if (unitPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), unitPrice, "Unit price must be strictly positive.");
        }

        var bracket = AreaBrackets.Classify(totalAreaM2);
        return new PriceQuote(serviceTier, totalAreaM2, bracket, requiredWorkers, unitPrice, Vnd.Round(unitPrice * requiredWorkers));
    }
}
