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
        services.AddScoped<ISubscriptionRepository, EfSubscriptionRepository>();
        services.AddScoped<SubscriptionService>();
        services.AddScoped<ISubscriptionReminderRepository, EfSubscriptionReminderRepository>();
        services.AddScoped<SubscriptionReminderService>();
        services.AddScoped<IVerifiedPartnerRepository, EfVerifiedPartnerRepository>();
        services.AddScoped<VerifiedPartnerSync>();
        services.AddScoped<IAgencyBankRepository, EfAgencyBankRepository>();
        services.AddScoped<AgencyBankService>();
        services.AddScoped<IWorkerQuotaGuard>(sp => sp.GetRequiredService<SubscriptionService>());
        services.AddScoped<IWorkerImportRepository, EfWorkerImportRepository>();
        services.AddScoped<WorkerImportService>();
        services.AddScoped<IAgencyReportRepository, EfAgencyReportRepository>();
        services.AddScoped<AgencyReportService>();
        services.AddScoped<IAgencySwapRepository, EfAgencySwapRepository>();
        services.AddScoped<AgencySwapService>();
        services.AddHostedService<AgencySwapExpiryWorker>();
        services.AddScoped<IEscrowRepository, EfEscrowRepository>();
        services.AddScoped<EscrowService>();
        services.AddScoped<ISlaPenaltyService>(sp => sp.GetRequiredService<EscrowService>());
        services.AddScoped<IPaymentPurposeSettlement, EscrowDepositSettlement>();
        services.AddScoped<IRosterRepository, EfRosterRepository>();
        services.AddScoped<ShiftRosterService>();
        services.AddScoped<WorkerQuotaEnforcer>();
        // The IPN endpoint finds the handler of a SUBSCRIPTION payment by purpose (BE-M5-02).
        services.AddScoped<IPaymentPurposeSettlement, SubscriptionPaymentSettlement>();
        services.AddHostedService<SubscriptionExpiryWorker>();
        services.AddScoped<ICapacityRepository, EfCapacityRepository>();
        // The real port replaces the Fake (FakePortRegistration only adds a Fake when nothing is registered, BASE-03).
        services.AddScoped<IAgencyCapacityService, AgencyCapacityService>();

        // AGENCY-UAT-02: Worker retirement and replacement
        services.AddScoped<WorkerRetirementService>();
        services.AddScoped<WorkerRetirementSweepService>();
        services.AddHostedService<WorkerRetirementSweepWorker>();
    }
}
