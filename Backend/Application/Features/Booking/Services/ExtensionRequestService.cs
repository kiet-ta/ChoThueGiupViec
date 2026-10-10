using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Booking.Services;

/// <summary>
/// The customer asks the worker on site to stay longer (BR-08, "Làm lần 2"), contract booking.md 3.8. Creates a PENDING_PAYMENT
/// extension priced from the order's frozen unit price (Q01); the payment is <c>POST /api/payments/extensions/{id}/qr</c>.
/// </summary>
public sealed class ExtensionRequestService(
    IExtensionRepository extensions,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<BusinessRules> rules) : IExtensionRequestService
{
    private const decimal Step = 0.5m;
    private readonly BusinessRules _rules = rules.Value;

    public async Task<ExtensionDto> CreateAsync(int customerId, long orderId, CreateExtensionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var order = await extensions.GetOrderAsync(orderId, cancellationToken);
        if (order is null || order.CustomerId != customerId)
        {
            throw new NotFoundException("Order", orderId);
        }

        var assignment = await extensions.GetAssignmentOfOrderAsync(orderId, request.AssignmentId, cancellationToken)
            ?? throw new NotFoundException("Assignment", request.AssignmentId);

        if (order.OrderStatus != JobOrderStatus.Assigned
            || assignment.AssignmentStatus is not (JobAssignmentStatus.InProgress or JobAssignmentStatus.AwaitingAcceptance))
        {
            throw new BusinessRuleViolationException(
                "An extension needs an assigned order whose worker is on site.", ExtensionErrorCodes.InvalidState);
        }

        if (await extensions.ExtensionExistsAsync(orderId, cancellationToken))
        {
            throw new BusinessRuleViolationException("The order already has an extension.", ExtensionErrorCodes.ExtensionExists);
        }

        // The frozen unit price per worker per shift (total_amount / required_workers), pro rata of the shift length.
        var unitPrice = order.TotalAmount / order.RequiredWorkers;
        var extraAmount = Vnd.Round(unitPrice / _rules.Shift.MaxHours * request.ExtraHours);
        var now = clock.UtcNow;

        var extension = new JobOrderExtension
        {
            OrderId = orderId,
            WorkerId = assignment.WorkerId,
            ExtraHours = request.ExtraHours,
            ExtraAmount = extraAmount,
            WorkerDecision = WorkerDecisions.Pending,
            ExtStatus = ExtensionStatuses.PendingPayment,
            RequestedAt = now,
            CreatedAt = now,
        };
        extensions.Add(extension);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ExtensionDto(
            extension.ExtensionId, extension.OrderId, extension.WorkerId, extension.ExtraHours, extension.ExtraAmount,
            extension.ExtStatus, extension.WorkerDecision, extension.RequestedAt, extension.DecidedAt);
    }

    private void Validate(CreateExtensionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (request.AssignmentId <= 0)
        {
            errors["assignmentId"] = ["assignmentId must be greater than 0."];
        }

        var max = _rules.Shift.MaxHours;
        if (request.ExtraHours < Step || request.ExtraHours > max || request.ExtraHours % Step != 0)
        {
            errors["extraHours"] = [$"extraHours must be between {Step} and {max} in steps of {Step}."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }
}
