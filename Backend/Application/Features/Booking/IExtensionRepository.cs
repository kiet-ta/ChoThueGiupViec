using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Booking;

/// <summary>The part of JOB_ORDER an extension request needs.</summary>
public sealed record OrderForExtension(long OrderId, int CustomerId, JobOrderStatus OrderStatus, decimal TotalAmount, byte RequiredWorkers);

/// <summary>The part of JOB_ASSIGNMENT an extension request needs.</summary>
public sealed record AssignmentForExtension(long AssignmentId, long OrderId, int WorkerId, JobAssignmentStatus AssignmentStatus);

/// <summary>JOB_ORDER_EXTENSION access of the Booking module (BE-M2-08, contract booking.md 3.8).</summary>
public interface IExtensionRepository
{
    Task<OrderForExtension?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>The assignment when it belongs to the order, otherwise null.</summary>
    Task<AssignmentForExtension?> GetAssignmentOfOrderAsync(long orderId, long assignmentId, CancellationToken cancellationToken = default);

    /// <summary>JOB_ORDER_EXTENSION.order_id is UNIQUE: at most one extension per order.</summary>
    Task<bool> ExtensionExistsAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>Stages the row; the unit of work saves it.</summary>
    void Add(JobOrderExtension extension);
}
