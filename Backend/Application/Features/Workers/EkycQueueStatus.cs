namespace CommonService.Application.Features.Workers;

/// <summary>The three lists of the Admin eKYC audit page (contract workers.md 2.2.2).</summary>
public static class EkycQueueStatus
{
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";

    public static readonly IReadOnlyList<string> All = [Pending, Approved, Rejected];
}
