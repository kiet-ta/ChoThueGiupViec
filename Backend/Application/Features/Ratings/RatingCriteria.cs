namespace CommonService.Application.Features.Ratings;

/// <summary>Who writes the rating. A customer rates the worker; a worker rates the customer (BR-09, decisions Q14).</summary>
public enum RaterSide
{
    Customer,
    Worker,
}

/// <summary>
/// The fixed criteria of decisions Q14. The API uses camelCase, the database (criteria_json) keeps the snake_case
/// spelling of the decision text. Any other key, or a missing one, is rejected.
/// </summary>
public static class RatingCriteria
{
    private static readonly (string Api, string Stored)[] CustomerToWorker =
    [
        ("punctuality", "punctuality"),
        ("cleaningQuality", "cleaning_quality"),
        ("attitude", "attitude"),
    ];

    private static readonly (string Api, string Stored)[] WorkerToCustomer =
    [
        ("cooperation", "cooperation"),
        ("workingConditions", "working_conditions"),
    ];

    public static string RaterRole(RaterSide side) => side == RaterSide.Customer ? "CUSTOMER" : "WORKER";

    public static IReadOnlyList<(string Api, string Stored)> For(RaterSide side) =>
        side == RaterSide.Customer ? CustomerToWorker : WorkerToCustomer;
}
