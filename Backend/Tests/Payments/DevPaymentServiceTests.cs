using CommonService.Application.Exceptions;
using CommonService.Application.Features.Payments.Services;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.WebAPI.Controllers.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CommonService.Tests.Payments;

/// <summary>UAT-PAY: the development tool that finishes a Fake-gateway payment through the IPN path.</summary>
public sealed class DevPaymentServiceTests
{
    private sealed class Repo : PaymentRepositoryStub
    {
        public PaymentTransaction? Payment { get; set; }
        public override Task<PaymentTransaction?> GetPaymentAsync(long paymentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Payment is not null && Payment.PaymentId == paymentId ? Payment : null);
    }

    private sealed class Ipn : IIpnService
    {
        public IpnOutcome Outcome { get; set; } = IpnOutcome.Accepted;
        public Dictionary<string, string>? Payload { get; private set; }

        public Task<IpnOutcome> HandleAsync(IReadOnlyDictionary<string, string> payload, string rawBody, CancellationToken cancellationToken = default)
        {
            Payload = new Dictionary<string, string>(payload);
            return Task.FromResult(Outcome);
        }
    }

    private sealed class Query : IDevPaymentQuery
    {
        public Task<IReadOnlyList<DevPaymentDto>> ListPendingAsync(int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DevPaymentDto>>([new DevPaymentDto(1, "ESCROWDEPOSIT", null, null, 9, 5_000_000m, "FAKE-x", DateTime.UtcNow)]);
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static PaymentTransaction Payment(string reference = "FAKE-abc", decimal amount = 390000m) =>
        new() { PaymentId = 5, GatewayTxnRef = reference, Amount = amount, TxnStatus = PaymentStatus.Pending };

    private static (DevPaymentService Service, Ipn Ipn, Repo Repo) Create()
    {
        var repo = new Repo { Payment = Payment() };
        var ipn = new Ipn();
        return (new DevPaymentService(new Query(), repo, ipn), ipn, repo);
    }

    [Fact]
    public async Task Confirm_sends_the_fake_gateways_success_callback_for_the_payment()
    {
        var (service, ipn, _) = Create();

        var outcome = await service.ConfirmAsync(5);

        Assert.Equal(IpnOutcome.Accepted, outcome);
        Assert.Equal("FAKE-abc", ipn.Payload!["gatewayTxnRef"]);
        Assert.Equal("390000", ipn.Payload["amount"]);
        Assert.Equal("success", ipn.Payload["status"]);
        Assert.Equal("valid", ipn.Payload["signature"]);
    }

    [Fact]
    public async Task Confirm_of_an_unknown_payment_is_not_found()
    {
        var (service, _, _) = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => service.ConfirmAsync(99));
    }

    [Fact]
    public async Task Confirm_refuses_a_payment_that_is_not_from_the_fake_gateway()
    {
        var (service, ipn, repo) = Create();
        repo.Payment = Payment("MOMO-123");

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.ConfirmAsync(5));

        Assert.Equal(DevPaymentErrorCodes.NotFakeGateway, ex.Code);
        Assert.Null(ipn.Payload);
    }

    [Fact]
    public async Task Confirm_of_a_settled_payment_reports_already_processed()
    {
        var (service, ipn, _) = Create();
        ipn.Outcome = IpnOutcome.AlreadyProcessed;

        Assert.Equal(IpnOutcome.AlreadyProcessed, await service.ConfirmAsync(5));
    }

    [Fact]
    public async Task Confirm_surfaces_a_rejected_callback_as_a_business_error()
    {
        var (service, ipn, _) = Create();
        ipn.Outcome = IpnOutcome.Rejected;

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.ConfirmAsync(5));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task The_controller_is_not_found_outside_Development(string environment)
    {
        var (service, ipn, _) = Create();
        var controller = new DevPaymentsController(service, new Env(environment));

        Assert.IsType<NotFoundResult>(await controller.List(default));
        Assert.IsType<NotFoundResult>(await controller.Confirm(5, default));
        Assert.Null(ipn.Payload);
    }

    [Fact]
    public async Task The_controller_confirms_in_Development()
    {
        var (service, _, _) = Create();
        var controller = new DevPaymentsController(service, new Env(Environments.Development));

        Assert.IsType<OkObjectResult>(await controller.List(default));
        var ok = Assert.IsType<OkObjectResult>(await controller.Confirm(5, default));
        Assert.NotNull(ok.Value);
    }
}
