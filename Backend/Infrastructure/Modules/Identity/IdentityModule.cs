using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Modularity;
using CommonService.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Identity;

/// <summary>
/// Module registration for Identity (M1).
/// Self-registers via IModule without modifying Program.cs (BASE-02).
/// </summary>
public sealed class IdentityModule : IModule
{
    public string Name => "Identity";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Real password hasher wins over FakePasswordHasher registered later with TryAdd (BASE-03 / BASE-10)
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        // Seeders
        services.AddScoped<AdminSeeder>();
        services.AddScoped<DefaultDataSeeder>();

        // Startup hosted service
        services.AddHostedService<DatabaseSeederHostedService>();
    }
}
