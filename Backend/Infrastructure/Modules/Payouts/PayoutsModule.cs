using CommonService.Application.Features.Payouts;
using CommonService.Application.Features.Payouts.Services;
using CommonService.Infrastructure.Modularity;
using CommonService.Infrastructure.Modules.Disputes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Payouts;

/// <summary>Module registration for Payouts (M6). Self-registers via IModule (BASE-02).</summary>
public sealed class PayoutsModule : IModule
{
    public string Name => "Payouts";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPayoutRepository, EfPayoutRepository>();
        services.AddScoped<IPayoutBatchService, PayoutBatchService>();
        services.AddScoped<IWorkerEarningsRepository, EfWorkerEarningsRepository>();
        services.AddScoped<IWorkerEarningsService, WorkerEarningsService>();
        services.AddScoped<IPayoutExportService, PayoutExportService>();

        // The Disputes side of the penalty port (question P1); registered here until the Disputes module registers its own services.
        services.AddScoped<IPayoutPenaltySource, EfPayoutPenaltySource>();
    }
}
