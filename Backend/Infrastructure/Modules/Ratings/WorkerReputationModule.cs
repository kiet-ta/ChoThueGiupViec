using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Ratings;

/// <summary>
/// Registers the real IWorkerReputation (M6). A module of its own so it does not share a file with the Ratings
/// endpoints module; the real implementation wins over FakeWorkerReputation, which is added later with TryAdd.
/// </summary>
public sealed class WorkerReputationModule : IModule
{
    public string Name => "WorkerReputation";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IWorkerReputation, EfWorkerReputation>();
    }
}
