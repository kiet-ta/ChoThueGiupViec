using CommonService.Application.Features.Payments;
using CommonService.Application.Features.Payments.Services;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Payments;

public class PaymentsModule : IModule
{
    public string Name => "Payments";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentQrService, PaymentQrService>();
    }
}
