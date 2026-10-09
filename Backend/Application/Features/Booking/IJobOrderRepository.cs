using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Booking;

/// <summary>
/// Repository contract for JOB_ORDER (Booking module).
/// </summary>
public interface IJobOrderRepository : IRepository<JobOrder, long>
{
}