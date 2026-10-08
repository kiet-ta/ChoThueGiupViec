using CommonService.Application.Features.Dispatch;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using CommonService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Dispatch;

/// <summary>
/// JOB_ORDER repository of the Dispatch module.
/// </summary>
public class EfJobOrderRepository : EfRepository<JobOrder, long>, IJobOrderRepository
{
    public EfJobOrderRepository(AppDbContext context) : base(context)
    {
    }
}