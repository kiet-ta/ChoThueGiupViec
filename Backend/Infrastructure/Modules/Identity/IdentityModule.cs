using CommonService.Application.Features.Identity.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modularity;
using CommonService.Infrastructure.Persistence;
using CommonService.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Identity;

/// <summary>
/// Module registration for Identity (M1).
/// Self-registers via IModule without modifying Program.cs (BASE-02).
/// Configures JWT Bearer authentication, 4 authorization policies, and real ICurrentUser (BE-M1-02, Gate G2).
/// </summary>
public sealed class IdentityModule : IModule
{
    public string Name => "Identity";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Real password hasher wins over FakePasswordHasher registered later with TryAdd (BASE-03 / BASE-10)
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        // Token & OTP Services (BE-M1-01, BE-M1-02)
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IOtpService, OtpService>();

        // Admin / Partner email + password login with lockout (BE-M1-03)
        services.AddScoped<IPasswordLoginService, PasswordLoginService>();

        // Real ICurrentUser (ClaimsCurrentUser) wins over FakeCurrentUser registered later with TryAdd (BE-M1-02)
        services.AddScoped<ICurrentUser, ClaimsCurrentUser>();

        // Authentication scheme for Bearer JWT
        services.AddAuthentication("Bearer")
            .AddScheme<AuthenticationSchemeOptions, JwtBearerAuthenticationHandler>("Bearer", null);

        // Authorization policies (contract identity.md §1)
        services.AddAuthorization(options =>
        {
            options.AddPolicy("CustomerOnly", policy => policy.RequireRole(UserRole.Customer.ToString()));
            options.AddPolicy("WorkerOnly", policy => policy.RequireRole(UserRole.Worker.ToString()));
            options.AddPolicy("PartnerOnly", policy => policy.RequireRole(UserRole.Partner.ToString()));
            options.AddPolicy("AdminOnly", policy => policy.RequireRole(UserRole.Admin.ToString()));
        });

        // Seeders
        services.AddScoped<AdminSeeder>();
        services.AddScoped<DefaultDataSeeder>();

        // Startup hosted service
        services.AddHostedService<DatabaseSeederHostedService>();
    }
}
