using CommonService.Application.Features.Workers;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Workers;

/// <summary>
/// Module registration for Workers (M4). Self-registers via IModule (BASE-02).
/// </summary>
public sealed class WorkersModule : IModule
{
    public string Name => "Workers";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IWorkerRepository, EfWorkerRepository>();
        services.AddScoped<IWorkerProfileQuery, WorkerProfileQuery>();
        services.AddScoped<IImageQualityService, ImageQualityService>();
        services.AddScoped<IEkycProvider, EkycProvider>();
    }
}
