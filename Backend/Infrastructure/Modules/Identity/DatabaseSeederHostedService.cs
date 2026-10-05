using CommonService.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CommonService.Infrastructure.Modules.Identity;

/// <summary>
/// Hosted service that runs database seeders on application startup.
/// Seeds reference data in all environments, and dev admin strictly in Development only.
/// </summary>
public sealed class DatabaseSeederHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DatabaseSeederHostedService> _logger;

    public DatabaseSeederHostedService(
        IServiceScopeFactory scopeFactory,
        IHostEnvironment environment,
        ILogger<DatabaseSeederHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Seed reference data in all environments (decisions Q01, Q08, skills)
            var defaultSeeder = scope.ServiceProvider.GetRequiredService<DefaultDataSeeder>();
            await defaultSeeder.SeedAsync(context, cancellationToken);

            // Seed dev admin only in Development (overview §10)
            if (_environment.IsDevelopment())
            {
                var adminSeeder = scope.ServiceProvider.GetRequiredService<AdminSeeder>();
                await adminSeeder.SeedAsync(context, cancellationToken);
            }
            else
            {
                _logger.LogInformation("DatabaseSeederHostedService: Skipping AdminSeeder in '{Env}' environment.", _environment.EnvironmentName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DatabaseSeederHostedService: Error while executing database seeders on startup.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
