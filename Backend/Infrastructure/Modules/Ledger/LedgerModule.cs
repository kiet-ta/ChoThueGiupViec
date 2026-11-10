using CommonService.Application.Features.Ledger;
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
    }
}
