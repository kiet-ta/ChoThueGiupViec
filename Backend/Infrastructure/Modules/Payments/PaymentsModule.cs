using CommonService.Application.Features.Payments;
using CommonService.Application.Features.Payments.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Modularity;
using CommonService.Infrastructure.Modules.Payments.MoMo;
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
        services.AddScoped<IPaymentReadService, PaymentReadService>();
        services.AddScoped<RefundService>();
        services.AddScoped<IRefundService>(sp => sp.GetRequiredService<RefundService>());
        services.AddScoped<IExtensionRefundService>(sp => sp.GetRequiredService<RefundService>());
        services.AddHostedService<PaymentReconciliationWorker>();
        AddMoMoGateway(services, configuration);
    }

    /// <summary>
    /// The MoMo sandbox adapter replaces the Fake only when real-looking credentials are configured (user-secrets / environment, G-6);
    /// with the committed placeholders nothing is registered here and <c>AddFakePorts</c> supplies the Fake, exactly as before.
    /// Credentials together with a non-sandbox endpoint stop the startup: no real money, ever (G-1).
    /// </summary>
    public static void AddMoMoGateway(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(MoMoOptions.SectionName).Get<MoMoOptions>() ?? new MoMoOptions();
        if (!options.HasCredentials)
        {
            return;
        }

        if (!options.PointsAtSandbox)
        {
            throw new InvalidOperationException(
                $"MoMo:Endpoint must be an https URL of the sandbox host {MoMoOptions.SandboxHost} (decisions G-1: sandbox only, no real money).");
        }

        services.AddSingleton(options);
        services.AddHttpClient<MoMoPaymentGateway>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddTransient<IPaymentGateway>(sp => sp.GetRequiredService<MoMoPaymentGateway>());
    }
}
