namespace CommonService.Application.Interfaces.Ports;

/// <param name="Amount">Whole VND to give back (100 % or the 60 % of decisions Q10 / Q15).</param>
public sealed record RefundRequest(long OrderId, decimal Amount, string Reason);

public sealed record RefundResult(bool Succeeded, decimal RefundedAmount, string? Message);

/// <summary>Refund the customer of an order. Implemented by Payments (M2); consumed by Dispatch (M3) when no worker is found or an incident happens.</summary>
public interface IRefundService
{
    Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default);
}
