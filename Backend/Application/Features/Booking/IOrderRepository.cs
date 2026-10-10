using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Booking;

/// <summary>Writes of the Booking module to JOB_ORDER (BE-M2-03). The unit of work saves; <c>OrderId</c> is known after the first save.</summary>
public interface IOrderRepository
{
    void Add(JobOrder order);

    Task<bool> OrderCodeExistsAsync(string orderCode, CancellationToken cancellationToken = default);

    /// <summary>The order when it belongs to the customer (not tracked), otherwise null: unknown and someone else's look the same.</summary>
    Task<JobOrder?> GetOwnedAsync(int customerId, long orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The customer's orders from JOB_ORDER WHERE customer_id (0 JOIN, PRD 5.2), newest created_at first (ties: highest id first),
    /// optionally one status, one page; and the total number matching (contract booking.md 3.4).
    /// </summary>
    Task<(IReadOnlyList<JobOrder> Items, int Total)> ListByCustomerAsync(
        int customerId, CommonService.Domain.Enums.JobOrderStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>The order by id, TRACKED so the caller's unit of work saves its status change; null when unknown (BE-M2-07).</summary>
    Task<JobOrder?> GetForUpdateAsync(long orderId, CancellationToken cancellationToken = default);
}
