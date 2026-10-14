using CommonService.Application.Exceptions;
using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Booking.Services;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.WebAPI.Controllers.Booking;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace CommonService.Tests.Booking;

/// <summary>BE-M2-02a: Admin edits of PRICE_RULE, audited in the same unit of work (contract booking.md 4.1-4.2, decisions Q01, G-5).</summary>
public sealed class PriceRuleAdminServiceTests
{
    private const int AdminId = 5;
    private static readonly DateTime Now = new(2026, 10, 10, 3, 0, 0, DateTimeKind.Utc);

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => Now;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local.AddHours(-7), DateTimeKind.Utc);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(Now));
    }

    private sealed class InMemoryRules : IPriceRuleRepository
    {
        public List<PriceRule> Rows { get; } =
        [
            new() { RuleId = 2, ServiceTier = ServiceTier.Premium, AreaBracket = AreaBrackets.UpTo30, UnitPrice = 300000m, IsActive = true, UpdatedAt = Now.AddDays(-9) },
            new() { RuleId = 1, ServiceTier = ServiceTier.Economy, AreaBracket = AreaBrackets.Over80, UnitPrice = 260000m, IsActive = true, UpdatedAt = Now.AddDays(-9) },
            new() { RuleId = 3, ServiceTier = ServiceTier.Economy, AreaBracket = AreaBrackets.UpTo30, UnitPrice = 160000m, IsActive = true, UpdatedAt = Now.AddDays(-9) },
        ];

        public Task<PriceRule?> GetActiveAsync(ServiceTier serviceTier, string areaBracket, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.FirstOrDefault(r => r.ServiceTier == serviceTier && r.AreaBracket == areaBracket && r.IsActive));

        public Task<IReadOnlyList<PriceRule>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PriceRule>>(Rows);

        public Task<PriceRule?> GetForUpdateAsync(int ruleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.FirstOrDefault(r => r.RuleId == ruleId));
    }

    private sealed class PassThroughUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default) => action();
    }

    private sealed class FailingAuditLog : IAuditLog
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("audit table unavailable");
    }

    private static (PriceRuleAdminService Service, InMemoryRules Rules, FakeAuditLog Audit) Create()
    {
        var rules = new InMemoryRules();
        var audit = new FakeAuditLog();
        return (new PriceRuleAdminService(rules, audit, new PassThroughUnitOfWork(), new TestClock()), rules, audit);
    }

    [Fact]
    public async Task List_ReturnsEveryRule_WithTheContractShape()
    {
        var (service, _, _) = Create();

        var list = await service.ListAsync();

        Assert.Equal(3, list.Count);
        var economy = list.First(r => r.RuleId == 3);
        Assert.Equal("ECONOMY", economy.ServiceTier);
        Assert.Equal("PREMIUM", list.First(r => r.RuleId == 2).ServiceTier);
        Assert.Equal(AreaBrackets.UpTo30, economy.AreaBracket);
        Assert.Equal(160000m, economy.UnitPrice);
        Assert.True(economy.IsActive);
    }

    [Fact]
    public async Task Update_ChangesThePrice_AndWritesExactlyOneAuditEntryWithOldNewAndReason()
    {
        var (service, rules, audit) = Create();

        var result = await service.UpdateUnitPriceAsync(AdminId, 3, 175000m, "  Seasonal demand  ");

        Assert.Equal(175000m, result.UnitPrice);
        Assert.Equal(AdminId, result.UpdatedBy);
        Assert.Equal(Now, result.UpdatedAt);
        Assert.Equal(175000m, rules.Rows.Single(r => r.RuleId == 3).UnitPrice);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActorType.Admin, entry.ActorType);
        Assert.Equal(AdminId, entry.AdminId);
        Assert.Equal("PRICE_RULE", entry.EntityType);
        Assert.Equal("3", entry.EntityId);
        Assert.Equal("unit_price", entry.FieldName);
        Assert.Equal("160000", entry.OldValue);
        Assert.Equal("175000", entry.NewValue);
        Assert.Equal("Seasonal demand", entry.Reason);
    }

    [Fact]
    public async Task Update_WithTheSamePrice_ChangesAndWritesNothing()
    {
        var (service, rules, audit) = Create();
        var before = rules.Rows.Single(r => r.RuleId == 3).UpdatedAt;

        var result = await service.UpdateUnitPriceAsync(AdminId, 3, 160000m, "no real change");

        Assert.Equal(160000m, result.UnitPrice);
        Assert.Empty(audit.Entries);
        Assert.Equal(before, rules.Rows.Single(r => r.RuleId == 3).UpdatedAt);
        Assert.Null(rules.Rows.Single(r => r.RuleId == 3).UpdatedBy);
    }

    [Theory]
    [InlineData("0", "ok")]
    [InlineData("-1", "ok")]
    [InlineData("160000.5", "ok")]
    [InlineData("10000000000000000", "ok")]
    [InlineData("175000", "")]
    [InlineData("175000", "   ")]
    public async Task Update_RejectsABadPriceOrReason_AndChangesNothing(string price, string reason)
    {
        var (service, rules, audit) = Create();

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            service.UpdateUnitPriceAsync(AdminId, 3, decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture), reason));

        Assert.NotEmpty(error.Errors);
        Assert.Equal(160000m, rules.Rows.Single(r => r.RuleId == 3).UnitPrice);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Update_AcceptsTheLimits_Price9999999999999999_AndReasonOf255Characters()
    {
        var (service, _, audit) = Create();

        await service.UpdateUnitPriceAsync(AdminId, 3, 9_999_999_999_999_999m, new string('r', 255));

        Assert.Single(audit.Entries);
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateUnitPriceAsync(AdminId, 3, 100m, new string('r', 256)));
    }

    [Fact]
    public async Task Update_OfAnUnknownRule_IsNotFound()
    {
        var (service, _, audit) = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateUnitPriceAsync(AdminId, 999, 175000m, "x"));

        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Update_WhenTheAuditWriteFails_ThePriceIsNotChanged()
    {
        var rules = new InMemoryRules();
        var service = new PriceRuleAdminService(rules, new FailingAuditLog(), new PassThroughUnitOfWork(), new TestClock());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateUnitPriceAsync(AdminId, 3, 175000m, "x"));

        Assert.Equal(160000m, rules.Rows.Single(r => r.RuleId == 3).UnitPrice);
        Assert.Null(rules.Rows.Single(r => r.RuleId == 3).UpdatedBy);
    }

    [Fact]
    public async Task Update_AppliesToNewQuotesOnly_OldOrdersKeepTheirFrozenTotal()
    {
        var (service, rules, _) = Create();
        var pricing = new PricingService(rules);
        var before = await pricing.QuoteAsync(ServiceTier.Economy, 20m, 1);
        var frozenTotal = before.TotalAmount; // what JOB_ORDER.total_amount holds for an order created earlier

        await service.UpdateUnitPriceAsync(AdminId, 3, 175000m, "raise");
        var after = await pricing.QuoteAsync(ServiceTier.Economy, 20m, 1);

        Assert.Equal(160000m, frozenTotal);
        Assert.Equal(175000m, after.TotalAmount);
    }

    [Fact]
    public void Controller_IsAdminOnly()
    {
        var authorize = typeof(AdminPriceRulesController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>().Single();

        Assert.Equal("AdminOnly", authorize.Policy);
    }
}
