using CommonService.Application.Common.Options;
using CommonService.Application.Features.Payments;
using CommonService.Application.Features.Payments.Services;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CommonService.Tests.Payments;

public class SandboxPaymentAutoConfirmTests
{
    private class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private class DummyQuery : IDevPaymentQuery
    {
        public Task<IReadOnlyList<DevPaymentDto>> ListPendingAsync(int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DevPaymentDto>>([]);
    }

    private class DummyRepo : PaymentRepositoryStub
    {
        public override Task<PaymentTransaction?> GetPaymentAsync(long paymentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PaymentTransaction?>(new PaymentTransaction
            {
                PaymentId = paymentId,
                GatewayTxnRef = $"FAKE-{paymentId}",
                Amount = 100000m,
                TxnStatus = PaymentStatus.Pending,
            });
    }

    private class RecordingIpn : IIpnService
    {
        public List<Dictionary<string, string>> Handled { get; } = [];

        public Task<IpnOutcome> HandleAsync(IReadOnlyDictionary<string, string> payload, string rawBody, CancellationToken cancellationToken = default)
        {
            lock (Handled)
            {
                Handled.Add(new Dictionary<string, string>(payload));
            }
            return Task.FromResult(IpnOutcome.Accepted);
        }
    }

    private static (SandboxPaymentAutoConfirmService Service, RecordingIpn Ipn) Create(
        string envName = "Development",
        bool autoConfirm = true,
        int delaySeconds = 1)
    {
        var ipn = new RecordingIpn();
        var repo = new DummyRepo();
        var query = new DummyQuery();

        var services = new ServiceCollection();
        services.AddScoped<IIpnService>(_ => ipn);
        services.AddScoped<IPaymentRepository>(_ => repo);
        services.AddScoped<IDevPaymentQuery>(_ => query);
        services.AddScoped<DevPaymentService>();
        var provider = services.BuildServiceProvider();

        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules
        {
            Payments = new PaymentRules
            {
                SandboxAutoConfirm = autoConfirm,
                SandboxAutoConfirmDelaySeconds = delaySeconds,
            },
        });

        var service = new SandboxPaymentAutoConfirmService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new Env(envName),
            rules,
            NullLogger<SandboxPaymentAutoConfirmService>.Instance);

        return (service, ipn);
    }

    [Fact]
    public async Task ScheduleAutoConfirm_triggers_dev_payment_confirm_in_development()
    {
        var (service, ipn) = Create(envName: "Development", autoConfirm: true, delaySeconds: 1);

        service.ScheduleAutoConfirm(42, "FAKE-abcdef123");

        // Wait slightly longer than 1s delay
        await Task.Delay(1300);

        lock (ipn.Handled)
        {
            Assert.Single(ipn.Handled);
            Assert.Equal("FAKE-42", ipn.Handled[0]["gatewayTxnRef"]);
            Assert.Equal("success", ipn.Handled[0]["status"]);
        }
    }

    [Fact]
    public async Task ScheduleAutoConfirm_does_nothing_outside_development()
    {
        var (service, ipn) = Create(envName: "Production", autoConfirm: true, delaySeconds: 1);

        service.ScheduleAutoConfirm(42, "FAKE-abcdef123");
        await Task.Delay(200);

        Assert.Empty(ipn.Handled);
    }

    [Fact]
    public async Task ScheduleAutoConfirm_does_nothing_for_non_fake_reference()
    {
        var (service, ipn) = Create(envName: "Development", autoConfirm: true, delaySeconds: 1);

        service.ScheduleAutoConfirm(42, "MOMO-12345");
        await Task.Delay(200);

        Assert.Empty(ipn.Handled);
    }

    [Fact]
    public async Task ScheduleAutoConfirm_does_nothing_when_disabled()
    {
        var (service, ipn) = Create(envName: "Development", autoConfirm: false, delaySeconds: 1);

        service.ScheduleAutoConfirm(42, "FAKE-12345");
        await Task.Delay(200);

        Assert.Empty(ipn.Handled);
    }
}
