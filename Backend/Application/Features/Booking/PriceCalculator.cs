using CommonService.Domain.Enums;
using CommonService.Domain.ValueObjects;

namespace CommonService.Application.Features.Booking;

/// <param name="UnitPrice">Price per worker per shift (decisions Q01).</param>
/// <param name="TotalAmount">UnitPrice x PriceUnits, whole VND (G-2). Frozen into JOB_ORDER.total_amount by order creation.</param>
/// <param name="PriceUnits">Unit prices charged; equals RequiredWorkers except for an ECONOMY large area (D35: 1 worker, 2 units).</param>
/// <param name="IsContinuousWork">One worker who may need a 2nd consecutive job (D35).</param>
public record PriceQuote(
    ServiceTier ServiceTier,
    decimal TotalAreaM2,
    string AreaBracket,
    int RequiredWorkers,
    decimal UnitPrice,
    decimal TotalAmount,
    int PriceUnits = 0,
    bool IsContinuousWork = false);

/// <summary>Pure price rule of decisions Q01: total = unit price x required workers. No DB, I/O or clock.</summary>
public static class PriceCalculator
{
    private const int MinWorkers = 1;
    private const int MaxWorkers = 2;

    /// <param name="requiredWorkers">1 or 2 (PRD BR-01/BR-02, decided by the shift rule of BE-M2-01); anything else is rejected.</param>
    /// <param name="priceUnits">Unit prices to charge, 1 or 2; null = one per worker (decisions Q01).</param>
    /// <param name="isContinuousWork">D35 marker carried to the quote.</param>
    public static PriceQuote Calculate(ServiceTier serviceTier, decimal totalAreaM2, int requiredWorkers, decimal unitPrice, int? priceUnits = null, bool isContinuousWork = false)
    {
        if (requiredWorkers is < MinWorkers or > MaxWorkers)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredWorkers), requiredWorkers, "Required workers must be 1 or 2 (BR-01/BR-02).");
        }

        var units = priceUnits ?? requiredWorkers;
        if (units is < MinWorkers or > MaxWorkers)
        {
            throw new ArgumentOutOfRangeException(nameof(priceUnits), priceUnits, "Price units must be 1 or 2.");
        }

        if (unitPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), unitPrice, "Unit price must be strictly positive.");
        }

        var bracket = AreaBrackets.Classify(totalAreaM2);
        return new PriceQuote(serviceTier, totalAreaM2, bracket, requiredWorkers, unitPrice, Vnd.Round(unitPrice * units), units, isContinuousWork);
    }
}
