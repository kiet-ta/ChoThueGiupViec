namespace CommonService.Application.Features.Ratings;

/// <summary>
/// The numbers behind <c>IWorkerReputation</c> (BE-M6-01b), kept pure so the definitions live in one place.
/// </summary>
public static class ReputationFormula
{
    /// <summary>
    /// Average stars the customers gave the worker, 0.00-5.00, rounded to 2 decimals away from zero (decision G-2 style
    /// rounding); 0 when the worker has no customer rating yet.
    /// </summary>
    public static decimal RatingAverage(long starsSum, int ratingCount) =>
        ratingCount <= 0 ? 0m : Math.Round((decimal)starsSum / ratingCount, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// "Share of accepted jobs that ended COMPLETED" (comment of the port; contract ratings.md question M3, recommended
    /// default): <c>completed / (completed + cancelledByWorker)</c>, 0 to 1, 3 decimals, 0 when there is no such job.
    /// A job the worker accepted ends in one of those two states when the worker is the cause. Customer-caused ABSENT,
    /// INCIDENT, REASSIGNED, a system CANCELLED, OFFERED and the still-running states are not counted against the worker.
    /// To change the definition, change only this method.
    /// </summary>
    public static decimal SuccessRate(int completed, int cancelledByWorker)
    {
        var denominator = completed + cancelledByWorker;
        return denominator <= 0 ? 0m : Math.Round((decimal)completed / denominator, 3, MidpointRounding.AwayFromZero);
    }
}
