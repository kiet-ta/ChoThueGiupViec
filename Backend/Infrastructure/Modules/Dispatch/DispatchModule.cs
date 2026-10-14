using CommonService.Application.Features.Dispatch;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Dispatch;

/// <summary>
/// Module registration for Dispatch (M3). Self-registers via IModule (BASE-02). Everything is scoped because the engine works on the
/// request's <c>AppDbContext</c>; races are settled by conditional updates in <see cref="EfDispatchRepository"/>, not by in-process locks.
/// </summary>
public sealed class DispatchModule : IModule
{
    public string Name => "Dispatch";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDispatchRepository, EfDispatchRepository>();
        services.AddScoped<SteppedRadiusDispatchScanner>();
        services.AddScoped<DispatchOfferEngine>();

        // Worker field steps (contract dispatch.md 3.1): one EF repository behind the three read/write ports.
        services.AddScoped<EfFieldRepository>();
        services.AddScoped<IFieldCheckInRepository>(sp => sp.GetRequiredService<EfFieldRepository>());
        services.AddScoped<ICustomerAbsentRepository>(sp => sp.GetRequiredService<EfFieldRepository>());
        services.AddScoped<IWorkerDispatchQuery>(sp => sp.GetRequiredService<EfFieldRepository>());
        services.AddScoped<FieldCheckInService>();
        services.AddScoped<CustomerAbsentService>();
        services.AddScoped<WorkerDispatchService>();
        services.AddHostedService<OfferExpiryWorker>();
    }
}
