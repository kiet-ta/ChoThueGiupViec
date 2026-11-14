namespace CommonService.Domain.Enums;

/// <summary>How an assignment was accepted (decision D32, ACCEPT-01): the customer tapped, the 24-hour window ran out, or a verdict closed it. Stored in <c>JOB_ASSIGNMENT.completion_source</c> through <c>DbEnum</c>.</summary>
public enum CompletionSource
{
    Customer,
    Auto,
    Verdict,
}
