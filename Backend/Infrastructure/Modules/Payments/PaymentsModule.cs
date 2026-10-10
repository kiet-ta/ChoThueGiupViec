using CommonService.Application.Features.Payments;
using CommonService.Application.Features.Payments.Services;
using CommonService.Application.Interfaces.Ports;
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
        services.AddScoped<IPaymentSettlementService, PaymentSettlementService>();
        services.AddScoped<IIpnService, IpnService>();
        services.AddScoped<IPaymentReconciliationService, PaymentReconciliationService>();
        services.AddScoped<RefundService>();
        services.AddScoped<IRefundService>(sp => sp.GetRequiredService<RefundService>());
        services.AddScoped<IExtensionRefundService>(sp => sp.GetRequiredService<RefundService>());
        services.AddHostedService<PaymentReconciliationWorker>();
    }
}
