using CommonService.Application.Common.Options;
using CommonService.Application.Features.Agencies;
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
public class DispatchModule : IModule
{
    public string Name => "Dispatch";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDispatchRepository, EfDispatchRepository>();
        services.AddScoped<IPremiumStartRepository, EfPremiumStartRepository>();
        services.AddScoped<PremiumOrderCanceller>();
        services.AddScoped<RescueCancelService>();
        services.AddScoped<PremiumShiftStartService>();
        services.AddScoped<IPremiumReminderRepository, EfPremiumReminderRepository>();
        services.AddScoped<PremiumCheckInReminderService>();
        services.AddHostedService<PremiumStartSweepWorker>();
        services.AddScoped<EfDispatchRepository>();
        services.AddScoped<IMilestoneFailoverRepository>(sp => sp.GetRequiredService<EfDispatchRepository>());
        services.AddScoped<SteppedRadiusDispatchScanner>();
        services.AddScoped<DispatchOfferEngine>();
        services.AddScoped<MilestoneFailoverService>(sp => new MilestoneFailoverService(
            sp.GetRequiredService<IMilestoneFailoverRepository>(),
            sp.GetRequiredService<IAgencySwapRepository>(),
            sp.GetRequiredService<IAgencyCapacityService>(),
            sp.GetRequiredService<ISlaPenaltyService>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IMediator>(),
            sp.GetRequiredService<IUnitOfWork>(),
            sp.GetRequiredService<IOptions<BusinessRules>>(),
            sp.GetRequiredService<ILogger<MilestoneFailoverService>>(),
            sp.GetRequiredService<INotificationService>(),
            sp.GetRequiredService<PremiumOrderCanceller>()));

        // Worker field steps (contract dispatch.md 3.1): one EF repository behind the three read/write ports.
        services.AddScoped<EfFieldRepository>();
        services.AddScoped<IFieldCheckInRepository>(sp => sp.GetRequiredService<EfFieldRepository>());
        services.AddScoped<ICustomerAbsentRepository>(sp => sp.GetRequiredService<EfFieldRepository>());
        services.AddScoped<IWorkerDispatchQuery>(sp => sp.GetRequiredService<EfFieldRepository>());
        services.AddScoped<FieldCheckInService>();
        services.AddScoped<CustomerAbsentService>();
        services.AddScoped<WorkerDispatchService>();
        services.AddScoped<IWorkerCancelRepository, EfWorkerCancelRepository>(); // freelancer cancel (Q15, D35)
        services.AddScoped<WorkerCancelService>();
        services.AddScoped<ConsecutiveShiftFallbackService>();
        services.AddScoped<CustomerArrivalService>(); // customer confirms the arrival when the GPS is off (BR-04, #657)
        services.AddScoped<IAdminIncidentQuery, EfAdminIncidentQuery>(); // Admin incident monitor (read only)
        services.AddHostedService<OfferExpiryWorker>();
    }
}
