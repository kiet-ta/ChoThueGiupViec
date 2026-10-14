using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Payments;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Tests.Payments;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Agencies;

/// <summary>BE-M5-08: the agency's jobs and the Pro-only performance report (decisions Q08: FREE has no analytics).</summary>
public class AgencyReportTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 14);

    private sealed class InMemoryReport : IAgencyReportRepository
    {
        public List<AgencyJobRow> Jobs { get; } = [];
        public List<WorkerPerformanceRow> Rows { get; } = [];
        public (DateTime From, DateTime To)? LastPeriod { get; private set; }
        public int? LastAgency { get; private set; }

        public Task<(IReadOnlyList<AgencyJobRow> Items, int Total)> ListJobsAsync(
            int agencyId, JobAssignmentStatus? status, int skip, int take, CancellationToken cancellationToken = default)
        {
            LastAgency = agencyId;
            var all = Jobs.Where(j => status == null || j.AssignmentStatus == status).OrderByDescending(j => j.CreatedAt).ToList();
            return Task.FromResult<(IReadOnlyList<AgencyJobRow>, int)>((all.Skip(skip).Take(take).ToList(), all.Count));
        }

        public Task<IReadOnlyList<WorkerPerformanceRow>> PerformanceAsync(
            int agencyId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
        {
            LastAgency = agencyId;
            LastPeriod = (fromUtc, toUtc);
            return Task.FromResult<IReadOnlyList<WorkerPerformanceRow>>(Rows);
        }

        public Task<IReadOnlyDictionary<int, string>> GetWorkerNamesAsync(IReadOnlyCollection<int> workerIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, string>>(workerIds.ToDictionary(id => id, id => $"Worker {id}"));
    }

    /// <summary>Only the package lookups the report needs; everything else is unused here.</summary>
    private sealed class PackageOnlySubscriptions : ISubscriptionRepository
    {
        public bool Pro { get; set; }

        public Task<SubscriptionPackage?> GetPackageByCodeAsync(string packageCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<SubscriptionPackage?>(new SubscriptionPackage
            {
                PackageId = 1,
                PackageCode = "FREE",
                Tier = "FREE",
                WorkerQuota = 3,
                CommissionRate = 0.2m,
                IsActive = true,
            });

        public Task<IReadOnlyList<(PartnerSubscription Subscription, SubscriptionPackage Package)>> ListSubscriptionsAsync(
            int agencyId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<(PartnerSubscription, SubscriptionPackage)> list = Pro
                ?
                [(
                    new PartnerSubscription
                    {
                        SubscriptionId = 1, AgencyId = agencyId, PackageId = 2, SubStatus = "ACTIVE", StartDate = Today.AddDays(-5), EndDate = Today.AddDays(25),
                    },
                    new SubscriptionPackage { PackageId = 2, PackageCode = "PRO_MONTHLY", Tier = "PRO", WorkerQuota = 50, HasAnalytics = true, IsActive = true })]
                : [];
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<SubscriptionPackage>> ListPackagesAsync(bool activeOnly, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SubscriptionPackage?> GetPackageAsync(int packageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SubscriptionPackage?> GetPackageForUpdateAsync(int packageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> GetAgencyStatusAsync(int agencyId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PartnerSubscription?> FindPendingAsync(int agencyId, int packageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(PartnerSubscription subscription) => throw new NotSupportedException();
        public Task<PartnerSubscription?> GetForUpdateAsync(int subscriptionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<(int WorkerId, DateTime CreatedAt)>> ListCountedWorkersAsync(int agencyId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<int>> ListAgenciesWithWorkersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> LockWorkersAsync(IReadOnlyCollection<int> workerIds, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentTransaction?> FindPendingPaymentAsync(int subscriptionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Harness
    {
        public InMemoryReport Repo { get; } = new();
        public PackageOnlySubscriptions Subs { get; } = new();
        public FakeCurrentUser User { get; } = new();
        public AgencyReportService Service { get; }

        public Harness(bool pro)
        {
            Subs.Pro = pro;
            var clock = new FakeClock();
            clock.Set(Now);
            var stub = new PaymentRepositoryStub2();
            var subscriptions = new SubscriptionService(
                Subs, stub, new FakePaymentGateway(), stub, new FakeAuditLog(), User, clock, MicrosoftOptions.Create(new BusinessRules()));
            Service = new AgencyReportService(Repo, subscriptions, User, clock);
            User.SignIn(7, UserRole.Partner);
        }
    }

    private sealed class PaymentRepositoryStub2 : PaymentRepositoryStub;

    [Fact]
    public async Task Jobs_are_the_callers_with_worker_names_and_the_status_filter()
    {
        var h = new Harness(pro: false);
        h.Repo.Jobs.Add(new AgencyJobRow(1, 100, 11, ServiceTier.Premium, 1, JobAssignmentStatus.Completed, 400_000m, 0.2m, 320_000m, Now.AddDays(-2), Now));
        h.Repo.Jobs.Add(new AgencyJobRow(2, 101, 12, ServiceTier.Premium, 1, JobAssignmentStatus.Assigned, 400_000m, 0.2m, 320_000m, Now.AddDays(-1), null));

        var all = await h.Service.ListJobsAsync(null, 1, 20);
        var completed = await h.Service.ListJobsAsync("COMPLETED", 1, 20);

        Assert.Equal(7, h.Repo.LastAgency);
        Assert.Equal(2, all.Total);
        Assert.Equal(2, all.Items[0].AssignmentId); // newest first
        Assert.Equal("Worker 12", all.Items[0].WorkerName);
        Assert.Equal("ASSIGNED", all.Items[0].AssignmentStatus);
        Assert.Equal("PREMIUM", all.Items[0].ServiceTier);
        var one = Assert.Single(completed.Items);
        Assert.Equal(1, one.AssignmentId);
        await Assert.ThrowsAsync<ValidationException>(() => h.Service.ListJobsAsync("NOPE", 1, 20));
    }

    [Fact]
    public async Task The_performance_report_is_not_part_of_the_free_package()
    {
        var h = new Harness(pro: false);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.GetPerformanceAsync(null, null));

        Assert.Equal(ReportErrorCodes.FeatureNotInPlan, ex.Code);
    }

    [Fact]
    public async Task A_pro_agency_gets_the_report_with_rates_ordered_by_completed_jobs()
    {
        var h = new Harness(pro: true);
        h.Repo.Rows.Add(new WorkerPerformanceRow(11, "An", Assigned: 10, Completed: 8, Cancelled: 1, Absent: 0, Incident: 1, PayoutTotal: 2_560_000m));
        h.Repo.Rows.Add(new WorkerPerformanceRow(12, "Binh", Assigned: 4, Completed: 4, Cancelled: 0, Absent: 0, Incident: 0, PayoutTotal: 1_280_000m));
        h.Repo.Rows.Add(new WorkerPerformanceRow(13, "Chi", Assigned: 0, Completed: 0, Cancelled: 0, Absent: 0, Incident: 0, PayoutTotal: 0m));

        var report = await h.Service.GetPerformanceAsync(null, null);

        Assert.Equal(Today.AddDays(-29), report.From);
        Assert.Equal(Today, report.To);
        Assert.Equal(14, report.TotalJobs);
        Assert.Equal(3_840_000m, report.PayoutTotal);
        Assert.Equal([11, 12, 13], report.Workers.Select(w => w.WorkerId)); // 8 completed, 4, 0
        Assert.Equal(0.8m, report.Workers[0].CompletionRate);
        Assert.Equal(1m, report.Workers[1].CompletionRate);
        Assert.Equal(0m, report.Workers[2].CompletionRate);
    }

    [Fact]
    public async Task The_period_is_local_days_converted_to_utc_and_bounded()
    {
        var h = new Harness(pro: true);

        await h.Service.GetPerformanceAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 14));

        // 2026-10-01 00:00 Vietnam = 2026-09-30 17:00 UTC; the end is the start of the day after `to`.
        Assert.Equal(new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc), h.Repo.LastPeriod!.Value.From);
        Assert.Equal(new DateTime(2026, 10, 14, 17, 0, 0, DateTimeKind.Utc), h.Repo.LastPeriod!.Value.To);
        await Assert.ThrowsAsync<ValidationException>(() => h.Service.GetPerformanceAsync(new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 1)));
        await Assert.ThrowsAsync<ValidationException>(() => h.Service.GetPerformanceAsync(new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 14)));
    }

    [Fact]
    public async Task Only_a_partner_reads_the_agency_reports()
    {
        var h = new Harness(pro: true);
        h.User.SignIn(1, UserRole.Customer);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => h.Service.ListJobsAsync(null, 1, 20));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => h.Service.GetPerformanceAsync(null, null));
    }
}
