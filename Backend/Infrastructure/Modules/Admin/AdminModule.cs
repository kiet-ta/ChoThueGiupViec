using CommonService.Application.Features.Admin;
using CommonService.Application.Features.Admin.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Admin;

/// <summary>Module registration for Admin (M6). Self-registers via IModule (BASE-02).</summary>
public sealed class AdminModule : IModule
{
    public string Name => "Admin";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // The real audit log wins over FakeAuditLog, which FakePortRegistration adds later with TryAdd.
        services.AddScoped<IAuditLog, EfAuditLog>();

        // Read side of the same table (BE-M6-09b).
        services.AddScoped<IAuditLogReadRepository, EfAuditLogReadRepository>();
        services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();

        // Profile of the signed-in Admin (BE-M6-06a).
        services.AddScoped<IAdminProfileReader, EfAdminProfileReader>();
        services.AddScoped<IAdminProfileService, AdminProfileService>();

        // Super-Freelancer approve / revoke / auto-revoke (BE-M6-09c).
        services.AddScoped<ISuperFreelancerRepository, EfSuperFreelancerRepository>();
        services.AddScoped<ISuperFreelancerService, SuperFreelancerService>();
    }
}
