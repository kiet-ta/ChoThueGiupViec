using CommonService.Application.Features.Disputes;
using CommonService.Application.Features.Disputes.Dtos;
using CommonService.Application.Features.Disputes.Services;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Admin;
using CommonService.Infrastructure.Modules.Disputes;
using CommonService.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Disputes;

/// <summary>BE-M6-02b: the verdict of a dispute (contract disputes.md 2.3, decisions Q09, Q10, Q22 D3, G-2, G-5).</summary>
public class DisputeVerdictTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 4, 0, 0, DateTimeKind.Utc);
    private const long OrderId = 900;
    private const int AdminId = 3;
    private const int DisputeId = 1;

    private sealed class RecordingPublisher : IPublisher
    {
        public List<DisputeResolved> Events { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (notification is DisputeResolved e) Events.Add(e);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Publish((object)notification, cancellationToken);
    }

    private sealed class RefusingRefunds(string? message = "The wallet is closed.") : IRefundService
    {
        public int Calls { get; private set; }

        public Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new RefundResult(false, 0m, message));
        }
    }

    private sealed class TrackingUnitOfWork : IUnitOfWork
    {
        public int Committed { get; private set; }
        public int RolledBack { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await action();
                Committed++;
                return result;
            }
            catch
            {
                RolledBack++;
                throw;
            }
        }
    }

    private sealed class Harness
    {
        public DisputeServiceTests.MemoryDisputes Repo { get; } = new();
        public FakeRefundService Refunds { get; } = new();
        public FakeSlaPenaltyService Sla { get; } = new();
        public FakeAuditLog Audit { get; } = new();
        public TrackingUnitOfWork Uow { get; } = new();
        public RecordingPublisher Publisher { get; } = new();
        public IRefundService RefundPort { get; set; }
        public DisputeTicket Ticket { get; }

        public Harness(string status = DisputeConstants.InReview, string category = "QUALITY")
        {
            RefundPort = Refunds;
            Ticket = new DisputeTicket
            {
                DisputeId = DisputeId,
                OrderId = OrderId,
                RaisedBy = "CUSTOMER",
                Category = category,
                Description = "Dirty",
                DisputeStatus = status,
                SlaDueAt = Now.AddHours(10),
                CreatedAt = Now.AddHours(-2),
            };
            Repo.Tickets.Add(Ticket);
        }

        /// <summary>Freelancer assignments (no agency) as (id, gross).</summary>
        public Harness Freelancers(params (long Id, decimal Gross)[] rows)
        {
            Add(rows.Select(r => new DisputeVerdictAssignment(r.Id, 40 + (int)r.Id, null, r.Gross)));
            return this;
        }

        /// <summary>Agency assignments as (id, agency, gross).</summary>
        public Harness Agencies(params (long Id, int Agency, decimal Gross)[] rows)
        {
            Add(rows.Select(r => new DisputeVerdictAssignment(r.Id, 40 + (int)r.Id, r.Agency, r.Gross)));
            return this;
        }

        private void Add(IEnumerable<DisputeVerdictAssignment> rows)
        {
            if (!Repo.VerdictAssignments.TryGetValue(OrderId, out var list)) Repo.VerdictAssignments[OrderId] = list = [];
            list.AddRange(rows);
        }

        public DisputeVerdictService Service() =>
            new(Repo, RefundPort, Sla, Audit, Uow, new DisputeServiceTests.TestClock(Now), Publisher);
    }

    private static ResolveDisputeRequestDto Body(string? fault, decimal? amount = null, bool? lockWorker = null, string? note = "  Verified with photos  ") => new()
    {
        FaultParty = fault,
        CompensationAmount = amount,
        LockWorker = lockWorker,
        Note = note,
    };

    // ---- 404 / 409 ------------------------------------------------------------------------------

    [Fact]
    public async Task A_missing_ticket_is_a_404_and_nothing_is_called()
    {
        var h = new Harness().Freelancers((1, 260000m));

        var result = await h.Service().ResolveAsync(AdminId, 99, Body("FREELANCER", 1000m));

        Assert.Equal(404, result.StatusCode);
        Assert.Empty(h.Refunds.Requests);
        Assert.Equal(0, h.Repo.ResolveCalls);
    }

    [Theory]
    [InlineData(DisputeConstants.Resolved)]
    [InlineData(DisputeConstants.Dismissed)]
    public async Task A_decided_ticket_is_a_409_and_a_second_call_changes_nothing(string status)
    {
        var h = new Harness(status).Freelancers((1, 260000m));

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body("FREELANCER", 1000m));

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(status, h.Ticket.DisputeStatus);
        Assert.Empty(h.Refunds.Requests);
        Assert.Empty(h.Audit.Entries);
        Assert.Empty(h.Publisher.Events);
    }

    [Fact]
    public async Task Losing_the_race_to_another_admin_is_a_409_with_no_refund_audit_or_event()
    {
        var h = new Harness().Freelancers((1, 260000m));
        h.Repo.BeforeResolve = () => h.Ticket.DisputeStatus = DisputeConstants.Resolved; // the other admin got there between our read and the claim

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body("FREELANCER", 1000m));

        Assert.Equal(409, result.StatusCode);
        Assert.Empty(h.Refunds.Requests);
        Assert.Empty(h.Audit.Entries);
        Assert.Empty(h.Publisher.Events);
    }

    // ---- 400 ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("FREELANCER", null, null, "   ", "note")]
    [InlineData("FREELANCER", null, null, "TOOLONG", "note")]
    [InlineData("FREELANCER", -1d, null, "ok", "compensationAmount")]
    [InlineData("FREELANCER", 1000.5, null, "ok", "compensationAmount")]
    [InlineData("FREELANCER", 260001d, null, "ok", "compensationAmount")]
    [InlineData(null, 1000d, null, "ok", "compensationAmount")]
    [InlineData("CUSTOMER", 1000d, null, "ok", "compensationAmount")]
    [InlineData("AGENCY", null, true, "ok", "lockWorker")]
    [InlineData("CUSTOMER", null, true, "ok", "lockWorker")]
    [InlineData(null, null, true, "ok", "lockWorker")]
    [InlineData("TENANT", null, null, "ok", "faultParty")]
    [InlineData("", null, null, "ok", "faultParty")]
    public async Task Bad_input_is_a_400_with_the_field_name_and_nothing_is_decided(
        string? fault, double? amount, bool? lockWorker, string note, string field)
    {
        var h = new Harness().Freelancers((1, 260000m));
        if (note == "TOOLONG") note = new string('x', 256);

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body(fault, amount is null ? null : (decimal)amount, lockWorker, note));

        Assert.Equal(400, result.StatusCode);
        Assert.Contains(field, result.ValidationErrors!.Keys);
        Assert.Equal(DisputeConstants.InReview, h.Ticket.DisputeStatus);
        Assert.Equal(0, h.Repo.ResolveCalls);
        Assert.Empty(h.Refunds.Requests);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task A_note_of_exactly_255_characters_is_accepted()
    {
        var h = new Harness().Freelancers((1, 260000m));

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body("CUSTOMER", note: new string('x', 255)));

        Assert.True(result.Success);
    }

    [Fact]
    public async Task A_compensation_equal_to_the_value_of_the_order_is_accepted()
    {
        var h = new Harness().Freelancers((1, 260000m));

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body("FREELANCER", 260000m));

        Assert.True(result.Success);
        Assert.Equal(260000m, Assert.Single(h.Refunds.Requests).Amount);
    }

    [Fact]
    public async Task A_freelancer_verdict_on_an_order_with_only_agency_workers_is_a_400_and_the_other_way_round()
    {
        var onlyAgency = new Harness().Agencies((1, 5, 260000m));
        var a = await onlyAgency.Service().ResolveAsync(AdminId, DisputeId, Body("FREELANCER"));
        Assert.Equal(400, a.StatusCode);
        Assert.Contains("faultParty", a.ValidationErrors!.Keys);

        var onlyFreelancer = new Harness().Freelancers((1, 260000m));
        var b = await onlyFreelancer.Service().ResolveAsync(AdminId, DisputeId, Body("AGENCY"));
        Assert.Equal(400, b.StatusCode);
        Assert.Contains("faultParty", b.ValidationErrors!.Keys);
    }

    // ---- effects --------------------------------------------------------------------------------

    [Fact]
    public async Task A_freelancer_verdict_refunds_the_customer_once_decides_the_ticket_audits_once_and_publishes()
    {
        var h = new Harness().Freelancers((1, 260000m));

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body("freelancer", 100000m));

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("RESOLVED", result.Data!.DisputeStatus);
        Assert.Equal("FREELANCER", result.Data.FaultParty);
        Assert.Equal(100000m, result.Data.CompensationAmount);
        Assert.Equal(AdminId, result.Data.ResolvedBy);
        Assert.Equal(Now, result.Data.ResolvedAt);

        var refund = Assert.Single(h.Refunds.Requests);
        Assert.Equal(OrderId, refund.OrderId);
        Assert.Equal(100000m, refund.Amount);
        Assert.Empty(h.Sla.Requests); // only an agency costs SLA points

        var row = Assert.Single(h.Audit.Entries);
        Assert.Equal(AuditActorType.Admin, row.ActorType);
        Assert.Equal(AdminId, row.AdminId);
        Assert.Equal("DISPUTE_TICKET", row.EntityType);
        Assert.Equal("1", row.EntityId);
        Assert.Equal("dispute_status", row.FieldName);
        Assert.Equal("IN_REVIEW", row.OldValue);
        Assert.Equal("RESOLVED", row.NewValue);
        Assert.Equal("Verified with photos", row.Reason);

        var published = Assert.Single(h.Publisher.Events);
        Assert.Equal(DisputeId, published.DisputeId);
        Assert.Equal(1, published.AssignmentId);
        Assert.Equal(FaultParty.Freelancer, published.FaultParty);
        Assert.Equal(100000m, published.CustomerRefundAmount);
        Assert.Equal("Verified with photos", published.ResolutionNotes);
        Assert.Equal(Now, published.ResolvedAtUtc);
        Assert.Equal(1, h.Uow.Committed);
    }

    [Fact]
    public async Task A_freelancer_verdict_from_an_open_ticket_works_too_and_a_zero_compensation_refunds_nothing()
    {
        var h = new Harness(DisputeConstants.Open).Freelancers((1, 260000m));

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body("FREELANCER"));

        Assert.True(result.Success);
        Assert.Equal(0m, result.Data!.CompensationAmount);
        Assert.Empty(h.Refunds.Requests);
        Assert.Equal("OPEN", Assert.Single(h.Audit.Entries).OldValue);
    }

    [Fact]
    public async Task Lock_worker_with_a_freelancer_verdict_locks_the_worker_and_writes_one_audit_row_for_the_request_and_one_per_worker()
    {
        var h = new Harness().Freelancers((1, 260000m));

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body("FREELANCER", 50000m, lockWorker: true));

        Assert.True(result.Success);
        Assert.Equal([41], Assert.Single(h.Repo.LockRequests));
        Assert.Equal(["dispute_status", "worker_lock_requested", "work_status"], h.Audit.Entries.Select(e => e.FieldName).ToArray());
        Assert.Equal("true", h.Audit.Entries.ElementAt(1).NewValue);
        var workerRow = h.Audit.Entries.Last();
        Assert.Equal(("WORKER", "41", "LOCKED"), (workerRow.EntityType, workerRow.EntityId, workerRow.NewValue));
    }

    [Fact]
    public async Task Lock_worker_with_two_freelancers_locks_both_once_and_skips_one_that_is_already_locked()
    {
        var h = new Harness().Freelancers((1, 100000m), (2, 100000m));
        h.Repo.AlreadyLocked.Add(42);

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body("FREELANCER", null, lockWorker: true));

        Assert.True(result.Success);
        Assert.Equal([41, 42], Assert.Single(h.Repo.LockRequests));
        var workerRows = h.Audit.Entries.Where(e => e.EntityType == "WORKER").ToList();
        Assert.Equal("41", Assert.Single(workerRows).EntityId); // 42 was locked before: no row
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Without_lock_worker_no_worker_is_touched(bool? lockWorker)
    {
        var h = new Harness().Freelancers((1, 260000m));

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body("FREELANCER", 50000m, lockWorker: lockWorker));

        Assert.True(result.Success);
        Assert.Empty(h.Repo.LockRequests);
        Assert.DoesNotContain(h.Audit.Entries, e => e.EntityType == "WORKER");
    }

    [Fact]
    public async Task An_agency_verdict_refunds_the_customer_and_costs_the_agency_quality_complaint_points_with_its_share()
    {
        var h = new Harness().Agencies((1, 5, 300000m), (2, 6, 100000m));

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body("AGENCY", 200000m));

        Assert.True(result.Success);
        Assert.Equal(200000m, Assert.Single(h.Refunds.Requests).Amount); // one refund for the order

        Assert.Equal(2, h.Sla.Requests.Count);
        var byAgency = h.Sla.Requests.ToDictionary(r => r.AgencyId);
        Assert.All(byAgency.Values, r =>
        {
            Assert.Equal(SlaViolation.QualityComplaint, r.Violation);
            Assert.Equal(OrderId, r.OrderId);
            Assert.Equal(DisputeId, r.DisputeId);
            Assert.Equal(0m, r.RescueCost);
        });
        Assert.Equal(150000m, byAgency[5].CustomerRefundAmount); // 3/4 of 200000
        Assert.Equal(50000m, byAgency[6].CustomerRefundAmount);
    }

    [Fact]
    public async Task An_agency_verdict_with_no_money_still_costs_the_upheld_dispute_points_once_per_agency()
    {
        var h = new Harness().Agencies((1, 5, 100000m), (2, 5, 100000m));

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body("AGENCY"));

        Assert.True(result.Success);
        Assert.Empty(h.Refunds.Requests);
        var request = Assert.Single(h.Sla.Requests); // two assignments of the same agency: one penalty
        Assert.Equal(5, request.AgencyId);
    }

    [Fact]
    public async Task An_absence_fee_reversal_against_an_agency_refunds_but_costs_no_SLA_points_or_escrow()
    {
        var h = new Harness(category: "ABSENT_FEE").Agencies((1, 5, 260000m));

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body("AGENCY", 104000m));

        Assert.True(result.Success);
        Assert.Equal(104000m, Assert.Single(h.Refunds.Requests).Amount);
        Assert.Empty(h.Sla.Requests); // decision D9: only NO_SHOW, SHORTAGE and an upheld quality complaint cost SLA points
    }

    [Fact]
    public async Task A_customer_verdict_records_the_fault_and_moves_no_money()
    {
        var h = new Harness().Freelancers((1, 260000m));

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body("CUSTOMER"));

        Assert.True(result.Success);
        Assert.Equal("RESOLVED", result.Data!.DisputeStatus);
        Assert.Equal("CUSTOMER", result.Data.FaultParty);
        Assert.Equal(0m, result.Data.CompensationAmount);
        Assert.Empty(h.Refunds.Requests);
        Assert.Empty(h.Sla.Requests);
        Assert.Equal(FaultParty.Customer, Assert.Single(h.Publisher.Events).FaultParty);
    }

    [Fact]
    public async Task A_null_fault_party_dismisses_the_dispute_without_effects_and_stores_no_compensation()
    {
        var h = new Harness().Freelancers((1, 260000m));

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body(null));

        Assert.True(result.Success);
        Assert.Equal("DISMISSED", result.Data!.DisputeStatus);
        Assert.Null(result.Data.FaultParty);
        Assert.Null(result.Data.CompensationAmount);
        Assert.Empty(h.Refunds.Requests);
        Assert.Empty(h.Sla.Requests);
        Assert.Equal("DISMISSED", Assert.Single(h.Audit.Entries).NewValue);
        var published = Assert.Single(h.Publisher.Events);
        Assert.Null(published.FaultParty);
        Assert.Equal(0m, published.CustomerRefundAmount);
    }

    [Fact]
    public async Task A_refused_refund_is_a_502_and_the_unit_of_work_is_undone_with_no_penalty_audit_or_event()
    {
        var h = new Harness().Agencies((1, 5, 260000m));
        var refusing = new RefusingRefunds();
        h.RefundPort = refusing;

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body("AGENCY", 100000m));

        Assert.Equal(502, result.StatusCode);
        Assert.Equal("The wallet is closed.", result.ErrorMessage);
        Assert.Equal(1, refusing.Calls);
        Assert.Equal(1, h.Uow.RolledBack);
        Assert.Equal(0, h.Uow.Committed);
        Assert.Empty(h.Sla.Requests); // the refund comes first (Principle 0); a failed one stops everything after it
        Assert.Empty(h.Audit.Entries);
        Assert.Empty(h.Publisher.Events);
    }

    [Fact]
    public async Task The_compensation_is_split_by_gross_value_in_whole_VND_and_the_parts_add_up_and_every_assignment_gets_an_event()
    {
        // 300000 : 100000 : 100000 of 100001 -> 60001 (60000.6 rounded), 20000 (20000.2), remainder 20000; the agency one gets 0.
        var h = new Harness()
            .Freelancers((1, 300000m), (2, 100000m), (3, 100000m))
            .Agencies((4, 9, 50000m));

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body("FREELANCER", 100001m));

        Assert.True(result.Success);
        var shares = h.Publisher.Events.ToDictionary(e => e.AssignmentId, e => e.CustomerRefundAmount);
        Assert.Equal(4, shares.Count);
        Assert.Equal(60001m, shares[1]);
        Assert.Equal(20000m, shares[2]);
        Assert.Equal(20000m, shares[3]);
        Assert.Equal(0m, shares[4]);
        Assert.Equal(100001m, shares.Values.Sum());
    }

    [Fact]
    public async Task Rounding_never_makes_a_share_negative_or_lets_the_parts_exceed_the_total()
    {
        var h = new Harness().Freelancers((1, 100000m), (2, 100000m), (3, 100000m), (4, 100000m), (5, 100000m));

        var result = await h.Service().ResolveAsync(AdminId, DisputeId, Body("FREELANCER", 3m));

        Assert.True(result.Success);
        var shares = h.Publisher.Events.Select(e => e.CustomerRefundAmount).ToArray();
        Assert.All(shares, s => Assert.True(s >= 0m));
        Assert.Equal(3m, shares.Sum());
    }

    // ---- SQL Server -----------------------------------------------------------------------------

    private static DisputeVerdictService Real(AppDbContext db, DateTime now, FakeRefundService refunds, FakeSlaPenaltyService sla, RecordingPublisher publisher) =>
        new(new EfDisputeRepository(db), refunds, sla, new EfAuditLog(db, new DisputeEndpointTests.FixedClock(now)),
            new UnitOfWork(db), new DisputeEndpointTests.FixedClock(now), publisher);

    private static async Task<int> AddTicketAsync(long orderId, DateTime now, string status = DisputeConstants.InReview)
    {
        await using var db = new AppDbContext(DisputeEndpointTests.Options());
        var ticket = new DisputeTicket
        {
            OrderId = orderId,
            RaisedBy = "CUSTOMER",
            Category = "QUALITY",
            Description = "Dirty",
            EvidenceUrls = "[\"/u/a.jpg\"]",
            DisputeStatus = status,
            SlaDueAt = now.AddHours(40),
            CreatedAt = now,
        };
        db.DisputeTickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket.DisputeId;
    }

    [Fact]
    public async Task Real_database_a_freelancer_verdict_updates_the_ticket_and_writes_one_audit_row_in_one_transaction()
    {
        if (!DisputeEndpointTests.IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await DisputeEndpointTests.SeedAsync(now);
        try
        {
            var disputeId = await AddTicketAsync(s.OrderIds[0], now);
            var refunds = new FakeRefundService();
            var publisher = new RecordingPublisher();

            await using (var db = new AppDbContext(DisputeEndpointTests.Options()))
            {
                var result = await Real(db, now, refunds, new FakeSlaPenaltyService(), publisher)
                    .ResolveAsync(s.AdminId, disputeId, Body("FREELANCER", 100000m, lockWorker: true, note: "Photos show dirt"));

                Assert.True(result.Success);
                Assert.Equal("RESOLVED", result.Data!.DisputeStatus);
            }

            await using var verify = new AppDbContext(DisputeEndpointTests.Options());
            var row = await verify.DisputeTickets.AsNoTracking().SingleAsync(d => d.DisputeId == disputeId);
            Assert.Equal("RESOLVED", row.DisputeStatus);
            Assert.Equal(FaultParty.Freelancer, row.FaultParty);
            Assert.Equal(100000m, row.CompensationAmount);
            Assert.Equal(s.AdminId, row.ResolvedBy);
            Assert.NotNull(row.ResolvedAt);

            var audit = await verify.AdminAuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "DISPUTE_TICKET" && a.EntityId == disputeId.ToString()).OrderBy(a => a.LogId).ToListAsync();
            Assert.Equal(["dispute_status", "worker_lock_requested"], audit.Select(a => a.FieldName).ToArray());
            Assert.Equal("Photos show dirt", audit[0].Reason);

            // the worker of the order is really locked, with one audit row of its own
            Assert.Equal(WorkStatus.Locked, (await verify.Workers.AsNoTracking().SingleAsync(w => w.WorkerId == s.WorkerId)).WorkStatus);
            var workerAudit = await verify.AdminAuditLogs.AsNoTracking()
                .SingleAsync(a => a.EntityType == "WORKER" && a.EntityId == s.WorkerId.ToString());
            Assert.Equal(("work_status", "LOCKED"), (workerAudit.FieldName, workerAudit.NewValue));
            Assert.Equal(100000m, Assert.Single(refunds.Requests).Amount);

            var published = Assert.Single(publisher.Events);
            Assert.Equal(s.AssignmentIds[0], published.AssignmentId);
            Assert.Equal(100000m, published.CustomerRefundAmount);
        }
        finally
        {
            await CleanVerdictAsync(s, s.OrderIds);
        }
    }

    [Fact]
    public async Task Real_database_lock_freelancers_locks_a_worker_once_and_skips_one_that_is_already_locked()
    {
        if (!DisputeEndpointTests.IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await DisputeEndpointTests.SeedAsync(now);
        try
        {
            await using var db = new AppDbContext(DisputeEndpointTests.Options());
            var repo = new EfDisputeRepository(db);

            Assert.Equal([s.WorkerId], await repo.LockFreelancersAsync([s.WorkerId, s.WorkerId]));
            Assert.Empty(await repo.LockFreelancersAsync([s.WorkerId]));
            Assert.Empty(await repo.LockFreelancersAsync([]));

            await using var verify = new AppDbContext(DisputeEndpointTests.Options());
            Assert.Equal(WorkStatus.Locked, (await verify.Workers.AsNoTracking().SingleAsync(w => w.WorkerId == s.WorkerId)).WorkStatus);
        }
        finally
        {
            await CleanVerdictAsync(s, s.OrderIds);
        }
    }

    [Fact]
    public async Task Real_database_a_refused_refund_rolls_the_ticket_and_the_audit_row_back()
    {
        if (!DisputeEndpointTests.IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await DisputeEndpointTests.SeedAsync(now);
        try
        {
            var disputeId = await AddTicketAsync(s.OrderIds[0], now);
            var publisher = new RecordingPublisher();

            await using (var db = new AppDbContext(DisputeEndpointTests.Options()))
            {
                var service = new DisputeVerdictService(
                    new EfDisputeRepository(db), new RefusingRefunds(), new FakeSlaPenaltyService(),
                    new EfAuditLog(db, new DisputeEndpointTests.FixedClock(now)), new UnitOfWork(db),
                    new DisputeEndpointTests.FixedClock(now), publisher);

                var result = await service.ResolveAsync(s.AdminId, disputeId, Body("FREELANCER", 100000m));

                Assert.Equal(502, result.StatusCode);
            }

            await using var verify = new AppDbContext(DisputeEndpointTests.Options());
            var row = await verify.DisputeTickets.AsNoTracking().SingleAsync(d => d.DisputeId == disputeId);
            Assert.Equal("IN_REVIEW", row.DisputeStatus);
            Assert.Null(row.FaultParty);
            Assert.Null(row.ResolvedAt);
            Assert.Equal(0, await verify.AdminAuditLogs.CountAsync(a => a.EntityType == "DISPUTE_TICKET" && a.EntityId == disputeId.ToString()));
            Assert.Empty(publisher.Events);
        }
        finally
        {
            await CleanVerdictAsync(s, s.OrderIds);
        }
    }

    [Fact]
    public async Task Real_database_six_simultaneous_verdicts_give_one_200_five_409_one_refund_and_one_audit_row()
    {
        if (!DisputeEndpointTests.IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await DisputeEndpointTests.SeedAsync(now);
        try
        {
            var disputeId = await AddTicketAsync(s.OrderIds[0], now);
            var refunds = new FakeRefundService();
            using var gate = new ManualResetEventSlim(false);

            async Task<int> Decide()
            {
                await using var db = new AppDbContext(DisputeEndpointTests.Options());
                var service = Real(db, now, refunds, new FakeSlaPenaltyService(), new RecordingPublisher());
                gate.Wait();
                return (await service.ResolveAsync(s.AdminId, disputeId, Body("FREELANCER", 100000m))).StatusCode;
            }

            var tasks = Enumerable.Range(0, 6).Select(_ => Task.Run(Decide)).ToArray();
            await Task.Delay(300);
            gate.Set();
            var codes = await Task.WhenAll(tasks);

            Assert.Equal(1, codes.Count(c => c == 200));
            Assert.Equal(5, codes.Count(c => c == 409));
            Assert.Single(refunds.Requests); // the losers never reach the refund
            await using var verify = new AppDbContext(DisputeEndpointTests.Options());
            Assert.Equal(1, await verify.AdminAuditLogs.CountAsync(a => a.EntityType == "DISPUTE_TICKET" && a.EntityId == disputeId.ToString()));
        }
        finally
        {
            await CleanVerdictAsync(s, s.OrderIds);
        }
    }

    [Fact]
    public async Task Real_database_a_dismissal_stores_no_compensation_and_the_order_total_limits_the_amount()
    {
        if (!DisputeEndpointTests.IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await DisputeEndpointTests.SeedAsync(now, orders: 2);
        try
        {
            var tooMuch = await AddTicketAsync(s.OrderIds[0], now);
            var dismissed = await AddTicketAsync(s.OrderIds[1], now, DisputeConstants.Open);

            await using var db = new AppDbContext(DisputeEndpointTests.Options());
            var service = Real(db, now, new FakeRefundService(), new FakeSlaPenaltyService(), new RecordingPublisher());

            var over = await service.ResolveAsync(s.AdminId, tooMuch, Body("FREELANCER", 260001m)); // the seeded gross is 260000
            Assert.Equal(400, over.StatusCode);

            var ok = await service.ResolveAsync(s.AdminId, dismissed, Body(null));
            Assert.True(ok.Success);
            Assert.Equal("DISMISSED", ok.Data!.DisputeStatus);

            await using var verify = new AppDbContext(DisputeEndpointTests.Options());
            var row = await verify.DisputeTickets.AsNoTracking().SingleAsync(d => d.DisputeId == dismissed);
            Assert.Null(row.CompensationAmount);
            Assert.Null(row.FaultParty);
            Assert.Equal("IN_REVIEW", (await verify.DisputeTickets.AsNoTracking().SingleAsync(d => d.DisputeId == tooMuch)).DisputeStatus);
        }
        finally
        {
            await CleanVerdictAsync(s, s.OrderIds);
        }
    }

    private static async Task CleanVerdictAsync(DisputeEndpointTests.Seeded s, List<long> orderIds)
    {
        await using (var db = new AppDbContext(DisputeEndpointTests.Options()))
        {
            var ids = await db.DisputeTickets.Where(d => orderIds.Contains(d.OrderId)).Select(d => d.DisputeId.ToString()).ToListAsync();
            await db.AdminAuditLogs.Where(a => a.EntityType == "DISPUTE_TICKET" && ids.Contains(a.EntityId)).ExecuteDeleteAsync();
            await db.AdminAuditLogs.Where(a => a.EntityType == "WORKER" && a.EntityId == s.WorkerId.ToString()).ExecuteDeleteAsync();
        }

        await DisputeEndpointTests.CleanAsync(s);
    }
}
