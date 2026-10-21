using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CommonService.Infrastructure.Modules.Workers;

/// <summary>
/// Module registration for Workers (M4). Self-registers via IModule (BASE-02).
/// </summary>
public sealed class WorkersModule : IModule
{
    public string Name => "Workers";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IWorkerRepository, EfWorkerRepository>();
        services.AddScoped<IWorkerProfileQuery, WorkerProfileQuery>();
        services.AddScoped<IImageQualityService, ImageQualityService>();
        services.AddScoped<IEkycProvider, EkycProvider>();
        services.AddScoped<IEkycAuditService, EkycAuditService>();
        services.AddScoped<IWorkerAvailabilityQuery, WorkerAvailabilityQuery>();

        services.AddScoped<IWorkerProfileService, WorkerProfileService>();
        services.AddScoped<IWorkerEkycService, WorkerEkycService>();
        services.AddScoped<IWorkerBookingSlotService, WorkerBookingSlotService>();
        services.AddScoped<IJobCompletionService, JobCompletionService>();
        services.AddScoped<IJobPhotoService, JobPhotoService>();
        services.AddScoped<IWorkerExtensionRepository, EfWorkerExtensionRepository>();
        services.AddScoped<IWorkerExtensionService, WorkerExtensionService>();

        // The legacy JSON photo upload (a URL judged by the Development stand-in) only exists in Development; elsewhere a file is required.
        services.AddSingleton(sp => new JobPhotoUploadPolicy(sp.GetRequiredService<IHostEnvironment>().IsDevelopment()));
    }
}


