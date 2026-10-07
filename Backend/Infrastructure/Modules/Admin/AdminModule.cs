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

        // Profile of the signed-in Admin (BE-M6-06a).
        services.AddScoped<IAdminProfileReader, EfAdminProfileReader>();
        services.AddScoped<IAdminProfileService, AdminProfileService>();
    }
}
