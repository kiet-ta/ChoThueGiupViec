using CommonService.Application.Common.Options;
using CommonService.Application.Features.Agencies;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using Microsoft.Extensions.Options;
using Xunit;

namespace CommonService.Tests.Agencies;

/// <summary>M5-VERIFIED-PARTNER: the label follows the Pro package, grace days included.</summary>
public class VerifiedPartnerTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);

    private sealed class Recording : IVerifiedPartnerRepository
    {
        public List<(DateOnly Today, int GraceDays, int? AgencyId)> Calls { get; } = [];

        public Task<int> SyncAsync(DateOnly today, int graceDays, int? agencyId, CancellationToken cancellationToken = default)
        {
            Calls.Add((today, graceDays, agencyId));
            return Task.FromResult(1);
        }
    }

    private static (VerifiedPartnerSync Sync, Recording Repo) Create()
    {
        var clock = new FakeClock();
        clock.Set(Now);
        var repo = new Recording();
        return (new VerifiedPartnerSync(repo, clock, Microsoft.Extensions.Options.Options.Create(new BusinessRules())), repo);
    }

    [Fact]
    public async Task Sync_uses_the_local_today_and_the_configured_grace_days()
    {
        var (sync, repo) = Create();

        await sync.SyncAsync();

        var call = Assert.Single(repo.Calls);
        Assert.Equal(new DateOnly(2026, 10, 14), call.Today);
        Assert.Equal(7, call.GraceDays);
        Assert.Null(call.AgencyId);
    }

    [Fact]
    public async Task An_activated_subscription_recomputes_the_label_of_its_agency_only()
    {
        var (sync, repo) = Create();
        var handler = new SubscriptionActivatedVerifiedPartnerHandler(sync);

        await handler.Handle(new SubscriptionActivated(5, 42, 2, "PRO_MONTHLY", Now, Now.AddDays(30)), default);

        Assert.Equal(42, Assert.Single(repo.Calls).AgencyId);
    }
}
