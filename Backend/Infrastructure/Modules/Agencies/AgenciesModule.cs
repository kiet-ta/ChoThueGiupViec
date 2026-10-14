using CommonService.Application.Features.Agencies;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>
/// Module registration for Agencies (M5). Self-registers via <see cref="IModule"/> (BASE-02).
/// </summary>
public sealed class AgenciesModule : IModule
{
    public string Name => "Agencies";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IAgencyRepository, EfAgencyRepository>();
        services.AddScoped<ICapacityRepository, EfCapacityRepository>();
        // The real port replaces the Fake (FakePortRegistration only adds a Fake when nothing is registered, BASE-03).
        services.AddScoped<IAgencyCapacityService, AgencyCapacityService>();
    }
}
