namespace CommonService.Application.Features.Customers.Dtos;

/// <summary>
/// One entry of the customer's favorite-workers list (contract customers.md §1).
/// Worker fields come from the read port IWorkerProfileQuery (decisions Q21 C3), addedAt from FAVORITE_WORKER.created_at.
/// </summary>
public sealed class FavoriteWorkerDto
{
    public int WorkerId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public decimal RatingAvg { get; set; }
    public int CompletedJobs { get; set; }

    /// <summary>Database spelling: PENDING, IDLE, BUSY or LOCKED.</summary>
    public string WorkStatus { get; set; } = string.Empty;

    public DateTime AddedAt { get; set; }
}
