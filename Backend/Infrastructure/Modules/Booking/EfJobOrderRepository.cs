using CommonService.Application.Features.Booking;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using CommonService.Infrastructure.Persistence.Repositories;

namespace CommonService.Infrastructure.Modules.Booking;

/// <summary>
/// EF Core implementation of IJobOrderRepository for the Booking module.
/// </summary>
public class EfJobOrderRepository : EfRepository<JobOrder, long>, IJobOrderRepository
{
    public EfJobOrderRepository(AppDbContext context) : base(context)
    {
    }
}