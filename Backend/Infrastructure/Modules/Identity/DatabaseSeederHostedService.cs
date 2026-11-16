using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CommonService.Infrastructure.Modules.Identity;

/// <summary>
/// Hosted service that runs database seeders on application startup.
/// Seeds reference data in all environments, and dev admin strictly in Development only.
/// In Development it first applies the pending EF migrations (BE-DEV-MIGRATE-01), which also creates the database when it is
/// missing, so a fresh local SQL Server runs the API from Visual Studio without a manual <c>dotnet ef database update</c>.
/// Staging and Production never migrate here (the Docker stack and releases use their own <c>migrate</c> step).
/// </summary>
public class DatabaseSeederHostedService : IHostedService
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

    /// <summary>Only the Development environment migrates (and creates) the database on start.</summary>
    public static bool ShouldMigrateOnStart(IHostEnvironment environment) => environment.IsDevelopment();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            if (ShouldMigrateOnStart(_environment))
            {
                var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
                if (pending > 0)
                {
                    _logger.LogInformation("DatabaseSeederHostedService: Applying {Count} pending migration(s) to the Development database.", pending);
                    await context.Database.MigrateAsync(cancellationToken);
                }
                else
                {
                    _logger.LogInformation("DatabaseSeederHostedService: The Development database is up to date.");
                }
            }

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
            // The target without the password, so a wrong server or a missing login is readable (BE-DEV-MIGRATE-01).
            var connection = context.Database.GetDbConnection();
            _logger.LogError(ex, "DatabaseSeederHostedService: Error while preparing the database '{Database}' on '{Server}' on startup. Check that SQL Server is running and that the login may create databases.",
                connection.Database, connection.DataSource);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
