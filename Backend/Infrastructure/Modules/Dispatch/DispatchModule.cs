using CommonService.Application.Common.Options;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Modularity;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CommonService.Infrastructure.Modules.Dispatch;

/// <summary>
/// Module registration for Dispatch (M3). Self-registers via IModule (BASE-02). Everything is scoped because the engine works on the
/// request's <c>AppDbContext</c>; races are settled by conditional updates in <see cref="EfDispatchRepository"/>, not by in-process locks.
/// </summary>
public sealed class DispatchModule : IModule
{
    public string Name => "Dispatch";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDispatchRepository, EfDispatchRepository>();
        services.AddScoped<EfDispatchRepository>();
        services.AddScoped<IMilestoneFailoverRepository>(sp => sp.GetRequiredService<EfDispatchRepository>());
        services.AddScoped<SteppedRadiusDispatchScanner>();
        services.AddScoped<DispatchOfferEngine>();
        services.AddScoped<MilestoneFailoverService>(sp => new MilestoneFailoverService(
            sp.GetRequiredService<IMilestoneFailoverRepository>(),
            sp.GetRequiredService<IAgencyCapacityService>(),
            sp.GetRequiredService<ISlaPenaltyService>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IMediator>(),
            sp.GetRequiredService<IUnitOfWork>(),
            sp.GetRequiredService<IOptions<BusinessRules>>(),
            sp.GetRequiredService<ILogger<MilestoneFailoverService>>()));

        // Worker field steps (contract dispatch.md 3.1): one EF repository behind the three read/write ports.
        services.AddScoped<EfFieldRepository>();
        services.AddScoped<IFieldCheckInRepository>(sp => sp.GetRequiredService<EfFieldRepository>());
        services.AddScoped<ICustomerAbsentRepository>(sp => sp.GetRequiredService<EfFieldRepository>());
        services.AddScoped<IWorkerDispatchQuery>(sp => sp.GetRequiredService<EfFieldRepository>());
        services.AddScoped<FieldCheckInService>();
        services.AddScoped<CustomerAbsentService>();
        services.AddScoped<WorkerDispatchService>();
        services.AddScoped<ConsecutiveShiftFallbackService>();
        services.AddScoped<IAdminIncidentQuery, EfAdminIncidentQuery>(); // Admin incident monitor (read only)
        services.AddHostedService<OfferExpiryWorker>();
    }
}
