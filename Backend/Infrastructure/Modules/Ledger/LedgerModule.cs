using CommonService.Application.Features.Ledger;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Ledger;

public class LedgerModule : IModule
{
    public string Name => "Ledger";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ILedgerRepository, EfLedgerRepository>();
        services.AddScoped<ILedgerWriter, LedgerWriter>();
        services.AddScoped<ILedgerChainVerifier, LedgerChainVerifier>();
        services.AddScoped<ILedgerQuery, EfLedgerQuery>();
        services.AddScoped<ILedgerBalanceService, LedgerBalanceService>();
        services.AddScoped<ILedgerSourceQuery, EfLedgerSourceQuery>();
        services.AddScoped<ILedgerPostingService, LedgerPostingService>();
        services.Configure<LedgerOptions>(configuration.GetSection("Ledger"));
        services.AddScoped<ILedgerReconciliationService, LedgerReconciliationService>();
        services.AddScoped<IWithdrawalRepository, EfWithdrawalRepository>();
        services.AddScoped<IWithdrawalService, WithdrawalService>();
        services.AddHostedService<LedgerReconciliationWorker>();
    }
}
