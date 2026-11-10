using CommonService.Application.Common.Models;
using CommonService.Application.Features.Payouts;
using CommonService.Application.Features.Payouts.Dtos;
using CommonService.Application.Features.Payouts.Services;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Payouts;
using CommonService.WebAPI.Controllers.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Payouts;

/// <summary>When the monthly settlement is due, the hourly worker that runs it, and the Development endpoint that runs it on demand (LEDGER-10).</summary>
public class PayoutSettlementWorkerTests
{
    private static FakeClock ClockAtLocal(int y, int m, int d, int h, int min, int s = 0)
    {
        var clock = new FakeClock();
        clock.Set(new DateTime(y, m, d, h, min, s, DateTimeKind.Utc).AddHours(-7)); // the given Asia/Ho_Chi_Minh wall time
        return clock;
    }

    // ---- the schedule ---------------------------------------------------------------------------

    [Theory]
    [InlineData(2026, 11, 10, 11, 0, 0, "2026-10")]
    [InlineData(2026, 11, 1, 0, 5, 0, "2026-10")] // exactly 00:05 on the first day: due
    [InlineData(2026, 11, 1, 0, 4, 59, null)] // not yet: the last minutes of October are still being written
    [InlineData(2026, 11, 1, 0, 0, 0, null)]
    [InlineData(2026, 10, 31, 23, 59, 59, "2026-09")] // October is not over, so the month due is still September's
    [InlineData(2027, 1, 1, 0, 10, 0, "2026-12")] // the year turns
    [InlineData(2027, 3, 1, 12, 0, 0, "2027-02")]
    public void The_previous_month_is_due_once_the_local_clock_is_past_five_minutes_after_midnight_of_the_first_day(
        int y, int m, int d, int h, int min, int s, string? expected)
    {
        Assert.Equal(expected, PayoutSettlementSchedule.DueMonth(ClockAtLocal(y, m, d, h, min, s)));
    }

    [Fact]
    public void The_local_calendar_decides_not_UTC()
    {
        // 2026-10-31 17:30 UTC is 00:30 on 1 November in Ho Chi Minh: October is over there and due.
        var clock = new FakeClock();
        clock.Set(new DateTime(2026, 10, 31, 17, 30, 0, DateTimeKind.Utc));

        Assert.Equal("2026-10", PayoutSettlementSchedule.DueMonth(clock));
    }

    [Fact]
    public void The_worker_looks_every_hour()
    {
        Assert.Equal(TimeSpan.FromHours(1), PayoutSettlementSchedule.CheckInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), PayoutSettlementSchedule.GracePeriod);
    }

    // ---- the worker -----------------------------------------------------------------------------

    private class RecordingBatches(int status = 201, bool throws = false) : IPayoutBatchService
    {
        public List<(string? Period, bool Allow)> Settled { get; } = [];

        public Task<PayoutResult<PayoutBatchDto>> SettleAsync(string? periodMonth, bool allowUnfinishedMonth = false, CancellationToken cancellationToken = default)
        {
            Settled.Add((periodMonth, allowUnfinishedMonth));
            if (throws) throw new InvalidOperationException("the database is down");
            return Task.FromResult(status switch
            {
                200 => PayoutResult<PayoutBatchDto>.Ok(new PayoutBatchDto { PeriodMonth = periodMonth ?? "", BatchStatus = "CLOSED" }),
                201 => PayoutResult<PayoutBatchDto>.Created(new PayoutBatchDto { PeriodMonth = periodMonth ?? "", BatchStatus = "CLOSED", ItemCount = 2, TotalAmount = 5m }),
                400 => PayoutResult<PayoutBatchDto>.ValidationError(new Dictionary<string, string[]> { ["periodMonth"] = ["bad"] }),
                _ => PayoutResult<PayoutBatchDto>.Conflict("1 payee(s) could not be settled"),
            });
        }

        public Task<PayoutResult<PayoutBatchPageDto>> ListAsync(string? page, string? pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PayoutResult<PayoutBatchDetailDto>> GetAsync(int batchId, string? payeeType, string? page, string? pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static PayoutSettlementWorker Worker(RecordingBatches batches, FakeClock clock)
    {
        var services = new ServiceCollection().AddSingleton<IPayoutBatchService>(batches).BuildServiceProvider();
        return new PayoutSettlementWorker(services.GetRequiredService<IServiceScopeFactory>(), clock, NullLogger<PayoutSettlementWorker>.Instance);
    }

    [Fact]
    public async Task When_the_month_is_due_the_worker_settles_the_previous_month_as_a_normal_run()
    {
        var batches = new RecordingBatches();

        var due = await Worker(batches, ClockAtLocal(2026, 11, 1, 1, 0)).RunOnceAsync(default);

        Assert.Equal("2026-10", due);
        Assert.Equal([("2026-10", false)], batches.Settled); // never the early-settlement switch of the demo endpoint
    }

    [Fact]
    public async Task In_the_first_five_minutes_of_the_month_the_worker_does_nothing()
    {
        var batches = new RecordingBatches();

        var due = await Worker(batches, ClockAtLocal(2026, 11, 1, 0, 3)).RunOnceAsync(default);

        Assert.Null(due);
        Assert.Empty(batches.Settled);
    }

    [Fact]
    public async Task Asking_again_is_safe_because_a_closed_month_answers_at_once_and_a_failure_never_stops_the_worker()
    {
        var closed = new RecordingBatches(status: 200);
        var worker = Worker(closed, ClockAtLocal(2026, 11, 2, 9, 0));
        await worker.RunOnceAsync(default);
        await worker.RunOnceAsync(default);
        Assert.Equal(2, closed.Settled.Count); // each look asks; the service does nothing for a closed month (PayoutBatchServiceTests)

        foreach (var broken in new[] { new RecordingBatches(throws: true), new RecordingBatches(status: 409), new RecordingBatches(status: 400) })
        {
            Assert.Equal("2026-10", await Worker(broken, ClockAtLocal(2026, 11, 2, 9, 0)).RunOnceAsync(default));
        }
    }

    [Fact]
    public void The_worker_is_registered_as_a_hosted_service_of_the_payouts_module()
    {
        var services = new ServiceCollection();
        new PayoutsModule().ConfigureServices(services, new ConfigurationBuilder().Build());

        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(PayoutSettlementWorker));
        Assert.Equal(TimeSpan.FromMinutes(1), PayoutSettlementWorker.StartupDelay);
    }

    // ---- the Development endpoint ---------------------------------------------------------------

    private class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public async Task The_dev_endpoint_settles_a_month_even_when_it_is_not_over_and_answers_the_batch_status()
    {
        var batches = new RecordingBatches();

        var result = Assert.IsType<OkObjectResult>(await new DevSettlementController(batches, new Env("Development")).Run("2026-11", default));

        Assert.Equal([("2026-11", true)], batches.Settled);
        var body = Assert.IsType<ApiResponse<DevSettlementDto>>(result.Value);
        Assert.Equal(("2026-11", "CLOSED"), (body.Data!.PeriodMonth, body.Data.BatchStatus));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Outside_Development_the_dev_endpoint_is_a_404_and_settles_nothing(string environment)
    {
        var batches = new RecordingBatches();

        Assert.IsType<NotFoundResult>(await new DevSettlementController(batches, new Env(environment)).Run("2026-10", default));
        Assert.Empty(batches.Settled);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(409)]
    public async Task The_dev_endpoint_passes_a_bad_month_or_a_failed_payee_through(int status)
    {
        var result = Assert.IsType<ObjectResult>(await new DevSettlementController(new RecordingBatches(status), new Env("Development")).Run("x", default));

        Assert.Equal(status, result.StatusCode);
        Assert.False(Assert.IsType<ApiResponse<object>>(result.Value).Success);
    }

    [Fact]
    public void The_dev_endpoint_is_anonymous_hidden_from_openapi_and_on_the_contract_route()
    {
        var type = typeof(DevSettlementController);

        Assert.NotNull(type.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).SingleOrDefault());
        Assert.True(((ApiExplorerSettingsAttribute)type.GetCustomAttributes(typeof(ApiExplorerSettingsAttribute), true).Single()).IgnoreApi);
        Assert.Equal("api/v1/dev/settlement", ((RouteAttribute)type.GetCustomAttributes(typeof(RouteAttribute), true).Single()).Template);
        var run = type.GetMethod(nameof(DevSettlementController.Run))!;
        Assert.Equal("run", ((HttpPostAttribute)run.GetCustomAttributes(typeof(HttpPostAttribute), true).Single()).Template);
    }
}
