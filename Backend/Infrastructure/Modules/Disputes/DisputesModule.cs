using CommonService.Application.Features.Disputes;
using CommonService.Application.Features.Disputes.Services;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Disputes;

/// <summary>Module registration for Disputes (M6). Self-registers via IModule (BASE-02).</summary>
public sealed class DisputesModule : IModule
{
    public string Name => "Disputes";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Thresholds (file window, SLA, priority bands) can be overridden in the "Disputes" configuration section.
        services.Configure<DisputeOptions>(configuration.GetSection("Disputes"));

        services.AddScoped<IDisputeRepository, EfDisputeRepository>();
        services.AddScoped<IDisputeFilingService, DisputeFilingService>();
        services.AddScoped<IAdminDisputeService, AdminDisputeService>();
    }
}
