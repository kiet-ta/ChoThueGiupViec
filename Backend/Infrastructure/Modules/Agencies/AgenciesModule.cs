using Backend.Application.Features.Agencies;
using Backend.Infrastructure.Modules.Agencies;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Infrastructure.Modules.Agencies;

/// <summary>
/// Module registration for Agencies (M5). Self-registers via IModule (BASE-02).
/// </summary>
public sealed class AgenciesModule : IModule
{
    public string Name => "Agencies";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IAgencyRepository, EfAgencyRepository>();
    }
}