using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.IServices;
using CommonService.Application.Services;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modularity;
using CommonService.Infrastructure.Persistance;
using CommonService.Infrastructure.Services;

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

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();

        // Services
        services.AddScoped<IEmailService, EmailService>();

        // Nếu muốn Redis
        // var redisConnection = config.GetConnectionString("Redis");
        // services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnection));
        // services.AddScoped<ICacheService, RedisCacheService>();

        // Business modules register themselves (IModule). Do not add per-module lines here.
        services.AddModules(configuration, typeof(DependencyInjection).Assembly);

        // Keep this call LAST: Fakes use TryAdd, so a real port implementation registered by a module
        // above wins and only a port nobody implemented yet falls back to its in-memory Fake (BASE-03).
        services.AddFakePorts();

        return services;
    }
}
