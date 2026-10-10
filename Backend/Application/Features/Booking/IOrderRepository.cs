using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Booking;

/// <summary>Writes of the Booking module to JOB_ORDER (BE-M2-03). The unit of work saves; <c>OrderId</c> is known after the first save.</summary>
public interface IOrderRepository
{
    void Add(JobOrder order);

    Task<bool> OrderCodeExistsAsync(string orderCode, CancellationToken cancellationToken = default);
}
