using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// Repository abstraction for JOB_ORDER entities.
/// </summary>
public interface IJobOrderRepository
{
    Task<JobOrder?> GetByIdAsync(long orderId, CancellationToken cancellationToken = default);
}