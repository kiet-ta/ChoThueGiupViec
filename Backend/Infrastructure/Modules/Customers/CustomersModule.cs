using CommonService.Application.Features.Customers;
using CommonService.Application.Features.Customers.Services;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Customers;

public class CustomersModule : IModule
{
    public string Name => "Customers";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICustomerProfileService, CustomerProfileService>();
    }
}
