using Backend.Application.Features.Agencies;
using Backend.Infrastructure.Modules.Agencies;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Infrastructure.Modules.Agencies;

/// <summary>
/// Agencies module for dependency injection registration
/// </summary>
public class AgenciesModule : IModule
{
    public string Name => "Agencies";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISubscriptionRepository, EfSubscriptionRepository>();
    }
}