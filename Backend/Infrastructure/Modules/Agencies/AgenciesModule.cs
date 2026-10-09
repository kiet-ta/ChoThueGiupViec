using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Modules.Agencies;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Agencies
{
    /// <summary>
    /// Agencies module service registrations.
    /// </summary>
    public class AgenciesModule : IModule
    {
        public string Name => "Agencies";

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IAgencyCapacityService, AgencyCapacityService>();
            services.AddScoped<IWorkerImportService, WorkerImportService>();
        }
    }
}