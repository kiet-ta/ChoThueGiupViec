using CommonService.Application.Interfaces.Ports;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CommonService.Infrastructure.Fakes;

public static class FakePortRegistration
{
    /// <summary>
    /// Register an in-memory Fake for every cross-module port with <c>TryAdd</c>. Call it AFTER the modules were added:
    /// a real implementation registered by a module (or by an earlier call) wins, a missing one falls back to its Fake.
    /// </summary>
    public static IServiceCollection AddFakePorts(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, FakeClock>();
        services.TryAddSingleton<ICurrentUser, FakeCurrentUser>();
        services.TryAddSingleton<IGeoService, FakeGeoService>();
        services.TryAddSingleton<IFileStorage, FakeFileStorage>();
        services.TryAddSingleton<IOtpSender, FakeOtpSender>();
        services.TryAddSingleton<IPasswordHasher, FakePasswordHasher>();
        services.TryAddSingleton<INotificationService, FakeNotificationService>();
        services.TryAddSingleton<IPaymentGateway, FakePaymentGateway>();
        services.TryAddSingleton<IRefundService, FakeRefundService>();
        services.TryAddSingleton<IAgencyCapacityService, FakeAgencyCapacityService>();
        services.TryAddSingleton<ISlaPenaltyService, FakeSlaPenaltyService>();
        services.TryAddSingleton<IWorkerAvailabilityQuery, FakeWorkerAvailabilityQuery>();
        services.TryAddSingleton<IWorkerReputation, FakeWorkerReputation>();
        services.TryAddSingleton<IAuditLog, FakeAuditLog>();
        services.TryAddSingleton<IEkycProvider, FakeEkycProvider>();
        services.TryAddSingleton<IImageQualityService, FakeImageQualityService>();
        services.TryAddSingleton<IWorkerProfileQuery, FakeWorkerProfileQuery>();
        services.TryAddSingleton<ICustomerAddressQuery, FakeCustomerAddressQuery>();

        // Fail the startup (not the first login) when the registered IOtpSender is the Fake outside Development.
        services.AddHostedService<OtpSenderStartupGuard>();
        return services;
    }
}
