using CommonService.Application.Common.Options;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.IServices;
using CommonService.Application.Services;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modularity;
using CommonService.Infrastructure.Persistence;
using CommonService.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CommonService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // In-Memory Cache
        services.AddMemoryCache();
        services.AddScoped<ICacheService, InMemoryCacheService>();

        // Persistence (BASE-07, SQL Server)
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Server=localhost;Database=ChoThueGiupViec;Integrated Security=True;TrustServerCertificate=True;";
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlServer(connectionString);
        });
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Services
        services.AddScoped<IEmailService, EmailService>();

        // Nếu muốn Redis
        // var redisConnection = config.GetConnectionString("Redis");
        // services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnection));
        // services.AddScoped<ICacheService, RedisCacheService>();

        // Business rules options (BASE-05, decisions.md section 4)
        services.Configure<BusinessRules>(configuration.GetSection(BusinessRules.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<BusinessRules>>().Value);

        // Business modules register themselves (IModule). Do not add per-module lines here.
        services.AddModules(configuration, typeof(DependencyInjection).Assembly);

        // Keep this call LAST: Fakes use TryAdd, so a real port implementation registered by a module
        // above wins and only a port nobody implemented yet falls back to its in-memory Fake (BASE-03).
        services.AddFakePorts();

        return services;
    }
}
