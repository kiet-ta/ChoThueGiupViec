namespace CommonService.Application.Interfaces.Ports;

/// <summary>Violation kinds of decisions Q09 (points: NO_SHOW -20, SHORTAGE -10, QUALITY_COMPLAINT -5, the last only for an upheld dispute).</summary>
public enum SlaViolation
{
    NoShow,
    Shortage,
    QualityComplaint
}

/// <param name="CustomerRefundAmount">Step 1 of the deduction order (decisions Q09): 100 % of the affected amount.</param>
/// <param name="RescueCost">Step 2: actual cost of the rescue worker.</param>
public sealed record SlaPenaltyRequest(
    int AgencyId,
    SlaViolation Violation,
    long? OrderId,
    int? DisputeId,
    decimal CustomerRefundAmount,
    decimal RescueCost,
    string Reason);

/// <param name="EscrowDeducted">What was actually taken (the balance never goes below 0).</param>
/// <param name="Shortfall">What the escrow could not cover.</param>
public sealed record SlaPenaltyResult(
    decimal SlaPointsDelta,
    decimal NewSlaScore,
    decimal EscrowDeducted,
    decimal Shortfall,
    bool AgencySuspended);

/// <summary>Deduct SLA points and escrow from an agency. Implemented by Agencies (M5); consumed by Dispatch (M3) and Disputes (M6).</summary>
public interface ISlaPenaltyService
{
    Task<SlaPenaltyResult> ApplyAsync(SlaPenaltyRequest request, CancellationToken cancellationToken = default);
}
