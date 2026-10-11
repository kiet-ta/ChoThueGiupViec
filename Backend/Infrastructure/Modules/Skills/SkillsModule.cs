using CommonService.Application.Features.Skills;
using CommonService.Infrastructure.Modules.Skills;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Skills;

/// <summary>
/// Skills module service registrations.
/// </summary>
public class SkillsModule : IModule
{
    public string Name => "Skills";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISkillRepository, EfSkillRepository>();
    }
}