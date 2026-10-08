using CommonService.Application.Features.Customers;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Dispatch;

/// <summary>
/// Dispatch module registration (BE-M3-10).
/// </summary>
public sealed class DispatchModule : IModule
{
    public string Name => "Dispatch";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IFieldCheckInRepository, EfFieldCheckInRepository>();
        services.AddScoped<IJobOrderRepository, EfJobOrderRepository>();
        services.AddScoped<ICustomerAddressRepository, CustomerAddressRepository>();
    }
}