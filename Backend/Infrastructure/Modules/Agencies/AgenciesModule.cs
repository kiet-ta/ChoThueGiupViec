using CommonService.Application.Features.Payments;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>
/// Module registration for Agencies (M5). Self-registers via <see cref="IModule"/> (BASE-02).
/// </summary>
public sealed class AgenciesModule : IModule
{
    public string Name => "Agencies";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IAgencyRepository, EfAgencyRepository>();
        services.AddScoped<IAgencySwapRepository, EfAgencySwapRepository>();
        services.AddScoped<AgencySwapService>();
        services.AddHostedService<AgencySwapExpiryWorker>();
        services.AddScoped<IEscrowRepository, EfEscrowRepository>();
        services.AddScoped<EscrowService>();
        // The real SLA penalty replaces the Fake (FakePortRegistration only adds a Fake when nothing is registered).
        services.AddScoped<ISlaPenaltyService>(sp => sp.GetRequiredService<EscrowService>());
        services.AddScoped<IPaymentPurposeSettlement, EscrowDepositSettlement>();
        services.AddScoped<ISubscriptionRepository, EfSubscriptionRepository>();
        services.AddScoped<SubscriptionService>();
        services.AddScoped<IWorkerQuotaGuard>(sp => sp.GetRequiredService<SubscriptionService>());
        services.AddScoped<IWorkerImportRepository, EfWorkerImportRepository>();
        services.AddScoped<WorkerImportService>();
        services.AddScoped<IRosterRepository, EfRosterRepository>();
        services.AddScoped<ShiftRosterService>();
        services.AddScoped<WorkerQuotaEnforcer>();
        // The IPN endpoint finds the handler of a SUBSCRIPTION payment by purpose (BE-M5-02).
        services.AddScoped<IPaymentPurposeSettlement, SubscriptionPaymentSettlement>();
        services.AddHostedService<SubscriptionExpiryWorker>();
    }
}
