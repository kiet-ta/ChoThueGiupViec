namespace CommonService.Application.Features.Booking.Services;

/// <summary>The <c>Extension</c> shape of contract booking.md section 2 (times in UTC).</summary>
public sealed record ExtensionDto(
    int ExtensionId,
    long OrderId,
    int WorkerId,
    decimal ExtraHours,
    decimal ExtraAmount,
    string ExtStatus,
    string WorkerDecision,
    DateTime RequestedAt,
    DateTime? DecidedAt);

/// <summary>Body of POST /api/booking/orders/{orderId}/extensions (contract booking.md 3.8).</summary>
public sealed record CreateExtensionRequest(long AssignmentId, decimal ExtraHours);

public static class ExtensionErrorCodes
{
    public const string InvalidState = "INVALID_STATE";
    public const string ExtensionExists = "EXTENSION_EXISTS";
}

public interface IExtensionRequestService
{
    /// <summary>
    /// "Làm lần 2" (BR-08): ValidationException (400), NotFoundException (404: order not the customer's, assignment not of the order),
    /// BusinessRuleViolationException with Code INVALID_STATE / EXTENSION_EXISTS (409).
    /// </summary>
    Task<ExtensionDto> CreateAsync(int customerId, long orderId, CreateExtensionRequest request, CancellationToken cancellationToken = default);
}
