namespace CommonService.Domain.ValueObjects;

/// <summary>
/// The single place for money rounding (decisions G-2, decision D5). Currency is VND only; every computed amount is
/// rounded to whole VND, half away from zero. All modules use this instead of calling Math.Round themselves.
/// </summary>
public static class Vnd
{
    /// <summary>Round to whole VND, half away from zero (2.5 -> 3, -2.5 -> -3).</summary>
    public static decimal Round(decimal amount) => Math.Round(amount, 0, MidpointRounding.AwayFromZero);

    /// <summary>Net payout = gross - round(gross * commissionRate) (decisions G-2). The rate is a fraction, e.g. 0.200.</summary>
    public static decimal Net(decimal gross, decimal commissionRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gross);
        if (commissionRate is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(commissionRate), commissionRate, "Commission rate must be between 0 and 1.");
        }

        return gross - Commission(gross, commissionRate);
    }

    /// <summary>Platform commission = round(gross * commissionRate).</summary>
    public static decimal Commission(decimal gross, decimal commissionRate) => Round(gross * commissionRate);
}
