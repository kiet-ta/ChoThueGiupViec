using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.IServices;
using CommonService.Application.Services;
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

        return services;
    }
}
