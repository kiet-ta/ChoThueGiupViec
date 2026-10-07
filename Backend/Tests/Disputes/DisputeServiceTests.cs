using System.Text.Json;
using CommonService.Application.Features.Disputes;
using CommonService.Application.Features.Disputes.Dtos;
using CommonService.Application.Features.Disputes.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CommonService.Tests.Disputes;

/// <summary>BE-M6-02a: filing, own lists, admin queue, case file and take (contract disputes.md, PRD 4.3) with an in-memory repository.</summary>
public class DisputeServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 4, 0, 0, DateTimeKind.Utc);
    private const int CustomerId = 11;
    private const int WorkerId = 22;
    private const long OrderId = 900;

    internal sealed class TestClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => local.AddHours(-7);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(UtcNow));
    }

    internal sealed class MemoryDisputes : IDisputeRepository
    {
        public Dictionary<long, DisputeOrderInfo> Orders { get; } = [];
        public List<DisputeTicket> Tickets { get; } = [];
        public Dictionary<long, DisputeSummaryData> Summaries { get; } = [];
        public DisputeCaseData Case { get; set; } = new([], [], []);
        public bool LoseTheRace { get; set; }
        public int Saves { get; private set; }
        public DisputeQueueFilter? LastFilter { get; private set; }
        public int LastPage { get; private set; }
        public int LastPageSize { get; private set; }
        private int _next = 1;

        public Task<DisputeOrderInfo?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.GetValueOrDefault(orderId));

        public Task<bool> TryAddAsync(DisputeTicket ticket, CancellationToken cancellationToken = default)
        {
            if (LoseTheRace || Tickets.Any(t => t.OrderId == ticket.OrderId)) return Task.FromResult(false);
            ticket.DisputeId = _next++;
            Tickets.Add(ticket);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<DisputeTicket>> ListForCustomerAsync(int customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DisputeTicket>>(Tickets.Where(t => Orders[t.OrderId].CustomerId == customerId).OrderByDescending(t => t.CreatedAt).ToList());

        public Task<IReadOnlyList<DisputeTicket>> ListForWorkerAsync(int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DisputeTicket>>(Tickets.Where(t => Orders[t.OrderId].Assignments.Any(a => a.WorkerId == workerId)).ToList());

        public Task<DisputeTicket?> GetForCustomerAsync(int disputeId, int customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Tickets.FirstOrDefault(t => t.DisputeId == disputeId && Orders[t.OrderId].CustomerId == customerId));

        public Task<DisputeTicket?> GetForWorkerAsync(int disputeId, int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Tickets.FirstOrDefault(t => t.DisputeId == disputeId && Orders[t.OrderId].Assignments.Any(a => a.WorkerId == workerId)));

        public Task<(IReadOnlyList<DisputeTicket> Items, int Total)> SearchAsync(
            DisputeQueueFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            LastFilter = filter;
            LastPage = page;
            LastPageSize = pageSize;
            var matching = Tickets
                .Where(t => filter.Statuses.Contains(t.DisputeStatus))
                .Where(t => filter.DueFromUtc is null || t.SlaDueAt >= filter.DueFromUtc)
                .Where(t => filter.DueBeforeUtc is null || t.SlaDueAt < filter.DueBeforeUtc)
                .OrderBy(t => t.SlaDueAt)
                .ToList();
            return Task.FromResult<(IReadOnlyList<DisputeTicket>, int)>((matching.Skip((page - 1) * pageSize).Take(pageSize).ToList(), matching.Count));
        }

        public Task<DisputeTicket?> FindTrackedAsync(int disputeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Tickets.FirstOrDefault(t => t.DisputeId == disputeId));

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<long, DisputeSummaryData>> GetSummaryDataAsync(IReadOnlyCollection<long> orderIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<long, DisputeSummaryData>>(Summaries.Where(s => orderIds.Contains(s.Key)).ToDictionary(s => s.Key, s => s.Value));

        public Task<DisputeCaseData> GetCaseDataAsync(long orderId, CancellationToken cancellationToken = default) => Task.FromResult(Case);

        public Dictionary<long, List<DisputeVerdictAssignment>> VerdictAssignments { get; } = [];
        public int ResolveCalls { get; private set; }
        public Action? BeforeResolve { get; set; }

        public Task<IReadOnlyList<DisputeVerdictAssignment>> GetVerdictAssignmentsAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DisputeVerdictAssignment>>(VerdictAssignments.GetValueOrDefault(orderId) ?? []);

        public Task<DisputeTicket?> TryResolveAsync(
            int disputeId, int adminId, string status, FaultParty? faultParty, decimal compensationAmount, DateTime resolvedAtUtc,
            CancellationToken cancellationToken = default)
        {
            ResolveCalls++;
            BeforeResolve?.Invoke(); // lets a test play "another admin decided it first"
            var ticket = Tickets.FirstOrDefault(t => t.DisputeId == disputeId);
            if (ticket is null || !DisputeConstants.Unresolved.Contains(ticket.DisputeStatus)) return Task.FromResult<DisputeTicket?>(null);
            ticket.DisputeStatus = status;
            ticket.FaultParty = faultParty;
            ticket.CompensationAmount = faultParty is null ? null : compensationAmount;
            ticket.ResolvedBy = adminId;
            ticket.ResolvedAt = resolvedAtUtc;
            return Task.FromResult<DisputeTicket?>(ticket);
        }
    }

    private sealed class Harness
    {
        public MemoryDisputes Repo { get; } = new();
        public TestClock Clock { get; } = new(Now);
        public DisputeFilingService Filing { get; }
        public AdminDisputeService Admin { get; }

        public Harness(JobAssignmentStatus status = JobAssignmentStatus.Completed)
        {
            // shift 08:00-12:00 local on 2026-10-07 = 01:00-05:00 UTC; completed 05:00 UTC; "now" is 04:00 UTC unless a test moves it
            Repo.Orders[OrderId] = new DisputeOrderInfo(OrderId, "ORD900", CustomerId,
                [new DisputeAssignmentInfo(1, WorkerId, status, status == JobAssignmentStatus.Completed ? Now.AddHours(1) : null, new DateTime(2026, 10, 7, 12, 0, 0))]);
            Filing = new DisputeFilingService(Repo, Clock, Microsoft.Extensions.Options.Options.Create(new DisputeOptions()));
            Admin = new AdminDisputeService(Repo, Clock, Microsoft.Extensions.Options.Options.Create(new DisputeOptions()));
        }
    }

    private static FileDisputeRequestDto Request(string category = "QUALITY", string? description = "Floor still dirty", params string[] evidence) => new()
    {
        OrderId = OrderId,
        Category = category,
        Description = description,
        EvidenceUrls = evidence.Length == 0 ? ["/uploads/a.jpg"] : [.. evidence],
    };

    // ---- filing: success ------------------------------------------------------------------------

    [Fact]
    public async Task A_customer_files_on_their_order_and_the_ticket_is_open_with_a_48h_SLA_and_JSON_evidence()
    {
        var h = new Harness();
        var result = await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, Request("PROPERTY_DAMAGE", "  Broken vase  ", "/u/1.jpg", "/u/2.jpg"));

        Assert.True(result.Success);
        Assert.Equal(201, result.StatusCode);
        var ticket = Assert.Single(h.Repo.Tickets);
        Assert.Equal("OPEN", ticket.DisputeStatus);
        Assert.Equal("CUSTOMER", ticket.RaisedBy);
        Assert.Equal("PROPERTY_DAMAGE", ticket.Category);
        Assert.Equal("Broken vase", ticket.Description);
        Assert.Equal(Now, ticket.CreatedAt);
        Assert.Equal(Now.AddHours(48), ticket.SlaDueAt);
        Assert.Null(ticket.FaultParty);
        Assert.Null(ticket.ResolvedBy);
        Assert.Equal(["/u/1.jpg", "/u/2.jpg"], JsonSerializer.Deserialize<string[]>(ticket.EvidenceUrls!)!);

        var dto = result.Data!;
        Assert.Equal(ticket.DisputeId, dto.DisputeId);
        Assert.Equal(["/u/1.jpg", "/u/2.jpg"], dto.EvidenceUrls);
        Assert.Equal(DateTimeKind.Utc, dto.SlaDueAt.Kind);
    }

    [Fact]
    public async Task A_worker_on_the_order_files_and_raised_by_is_WORKER()
    {
        var h = new Harness();
        var result = await h.Filing.FileAsync(DisputeSide.Worker, WorkerId, Request());
        Assert.Equal(201, result.StatusCode);
        Assert.Equal("WORKER", h.Repo.Tickets[0].RaisedBy);
    }

    [Theory]
    [InlineData(JobAssignmentStatus.Completed)]
    [InlineData(JobAssignmentStatus.AwaitingAcceptance)]
    [InlineData(JobAssignmentStatus.Absent)]
    public async Task Only_assignments_that_reached_acceptance_completion_or_absence_can_be_disputed(JobAssignmentStatus status)
    {
        var h = new Harness(status);
        Assert.Equal(201, (await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, Request())).StatusCode);
    }

    [Theory]
    [InlineData(JobAssignmentStatus.Offered)]
    [InlineData(JobAssignmentStatus.Assigned)]
    [InlineData(JobAssignmentStatus.CheckedIn)]
    [InlineData(JobAssignmentStatus.InProgress)]
    [InlineData(JobAssignmentStatus.Cancelled)]
    [InlineData(JobAssignmentStatus.CancelledByWorker)]
    [InlineData(JobAssignmentStatus.Incident)]
    [InlineData(JobAssignmentStatus.Reassigned)]
    public async Task An_order_with_nothing_to_dispute_yet_is_409(JobAssignmentStatus status)
    {
        var h = new Harness(status);
        var result = await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, Request());
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(h.Repo.Tickets);
    }

    // ---- filing: 24 h window --------------------------------------------------------------------

    [Fact]
    public async Task The_window_ends_24h_after_the_later_of_completion_and_shift_end_inclusive()
    {
        // completed 05:00 UTC, shift end 12:00 local = 05:00 UTC: both 05:00; window closes at 05:00 + 24 h
        var open = new Harness();
        open.Clock.UtcNow = Now.AddHours(1).AddHours(24);
        Assert.Equal(201, (await open.Filing.FileAsync(DisputeSide.Customer, CustomerId, Request())).StatusCode);

        var closed = new Harness();
        closed.Clock.UtcNow = Now.AddHours(1).AddHours(24).AddTicks(1);
        var result = await closed.Filing.FileAsync(DisputeSide.Customer, CustomerId, Request());
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("passed", result.ErrorMessage);
        Assert.Empty(closed.Repo.Tickets);
    }

    [Fact]
    public async Task An_absent_assignment_without_completion_uses_the_shift_end_and_a_late_completion_wins_over_it()
    {
        var absent = new Harness(JobAssignmentStatus.Absent);   // shift end 05:00 UTC, no completed_at
        absent.Clock.UtcNow = new DateTime(2026, 10, 8, 5, 0, 0, DateTimeKind.Utc);
        Assert.Equal(201, (await absent.Filing.FileAsync(DisputeSide.Customer, CustomerId, Request())).StatusCode);
        absent.Repo.Tickets.Clear();

        var late = new Harness();                               // completed later than the shift end
        late.Repo.Orders[OrderId] = new DisputeOrderInfo(OrderId, "ORD900", CustomerId,
            [new DisputeAssignmentInfo(1, WorkerId, JobAssignmentStatus.Completed, new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 7, 12, 0, 0))]);
        late.Clock.UtcNow = new DateTime(2026, 10, 8, 8, 59, 0, DateTimeKind.Utc);
        Assert.Equal(201, (await late.Filing.FileAsync(DisputeSide.Customer, CustomerId, Request())).StatusCode);
        late.Repo.Tickets.Clear();
        late.Clock.UtcNow = new DateTime(2026, 10, 8, 9, 1, 0, DateTimeKind.Utc);
        Assert.Equal(409, (await late.Filing.FileAsync(DisputeSide.Customer, CustomerId, Request())).StatusCode);
    }

    // ---- filing: ownership, uniqueness, validation ---------------------------------------------

    [Fact]
    public async Task An_order_that_is_not_the_callers_or_does_not_exist_is_404()
    {
        var h = new Harness();
        Assert.Equal(404, (await h.Filing.FileAsync(DisputeSide.Customer, CustomerId + 1, Request())).StatusCode);
        Assert.Equal(404, (await h.Filing.FileAsync(DisputeSide.Worker, WorkerId + 1, Request())).StatusCode);
        var missing = new FileDisputeRequestDto { OrderId = 12345, Category = "QUALITY", Description = "x", EvidenceUrls = ["/a"] };
        Assert.Equal(404, (await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, missing)).StatusCode);
        Assert.Empty(h.Repo.Tickets);
    }

    [Fact]
    public async Task One_ticket_per_order_the_second_filing_by_either_side_is_409_and_so_is_losing_a_race()
    {
        var h = new Harness();
        Assert.Equal(201, (await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, Request())).StatusCode);

        var byWorker = await h.Filing.FileAsync(DisputeSide.Worker, WorkerId, Request());
        Assert.Equal(409, byWorker.StatusCode);
        Assert.Contains("already exists", byWorker.ErrorMessage);
        Assert.Single(h.Repo.Tickets);

        var race = new Harness { Repo = { LoseTheRace = true } };
        Assert.Equal(409, (await race.Filing.FileAsync(DisputeSide.Customer, CustomerId, Request())).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-5L)]
    public async Task The_order_id_is_required(long? orderId)
    {
        var h = new Harness();
        var request = new FileDisputeRequestDto { OrderId = orderId, Category = "QUALITY", Description = "x", EvidenceUrls = ["/a"] };
        var result = await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, request);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("orderId", result.ValidationErrors!.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("quality")]
    [InlineData("BROKEN")]
    [InlineData("QUALITY ")]
    public async Task Only_the_five_categories_are_accepted_case_sensitively(string? category)
    {
        var h = new Harness();
        var result = await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, Request(category!));
        if (category == "QUALITY ") Assert.Equal(201, result.StatusCode); // trimmed
        else Assert.Contains("category", result.ValidationErrors!.Keys);
    }

    [Theory]
    [InlineData("QUALITY")]
    [InlineData("ATTITUDE")]
    [InlineData("PROPERTY_DAMAGE")]
    [InlineData("ABSENT_FEE")]
    [InlineData("OTHER")]
    public async Task Every_listed_category_is_accepted(string category)
    {
        var h = new Harness();
        Assert.Equal(201, (await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, Request(category))).StatusCode);
    }

    [Fact]
    public async Task The_description_is_required_and_at_most_1000_characters()
    {
        var h = new Harness();
        foreach (var bad in new[] { null, "", "   ", new string('a', 1001) })
        {
            var result = await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, Request("QUALITY", bad));
            Assert.Contains("description", result.ValidationErrors!.Keys);
        }

        Assert.Equal(201, (await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, Request("QUALITY", new string('a', 1000)))).StatusCode);
    }

    [Fact]
    public async Task Evidence_is_required_at_most_10_entries_each_1_to_500_characters()
    {
        var h = new Harness();

        var none = new FileDisputeRequestDto { OrderId = OrderId, Category = "QUALITY", Description = "x", EvidenceUrls = [] };
        Assert.Contains("evidenceUrls", (await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, none)).ValidationErrors!.Keys);

        var missing = new FileDisputeRequestDto { OrderId = OrderId, Category = "QUALITY", Description = "x" };
        Assert.Contains("evidenceUrls", (await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, missing)).ValidationErrors!.Keys);

        var tooMany = Request("QUALITY", "x", Enumerable.Range(0, 11).Select(i => $"/u/{i}").ToArray());
        Assert.Contains("evidenceUrls", (await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, tooMany)).ValidationErrors!.Keys);

        var blank = Request("QUALITY", "x", "/ok", "  ");
        Assert.Contains("evidenceUrls", (await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, blank)).ValidationErrors!.Keys);

        var longOne = Request("QUALITY", "x", "/" + new string('a', 500));
        Assert.Contains("evidenceUrls", (await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, longOne)).ValidationErrors!.Keys);

        Assert.Empty(h.Repo.Tickets);
        var ten = Request("QUALITY", "x", Enumerable.Range(0, 10).Select(i => $"/u/{i}").ToArray());
        Assert.Equal(201, (await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, ten)).StatusCode);
    }

    [Fact]
    public async Task Several_validation_problems_are_reported_together()
    {
        var h = new Harness();
        var result = await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, new FileDisputeRequestDto());
        Assert.Equal(["category", "description", "evidenceUrls", "orderId"], result.ValidationErrors!.Keys.Order().ToArray());
    }

    // ---- own lists ------------------------------------------------------------------------------

    [Fact]
    public async Task Each_party_lists_and_reads_only_their_own_dispute_and_the_admin_handler_is_hidden()
    {
        var h = new Harness();
        await h.Filing.FileAsync(DisputeSide.Customer, CustomerId, Request());
        h.Repo.Tickets[0].ResolvedBy = 77;
        var id = h.Repo.Tickets[0].DisputeId;

        Assert.Single((await h.Filing.ListAsync(DisputeSide.Customer, CustomerId)).Data!);
        Assert.Single((await h.Filing.ListAsync(DisputeSide.Worker, WorkerId)).Data!);
        Assert.Empty((await h.Filing.ListAsync(DisputeSide.Customer, CustomerId + 1)).Data!);
        Assert.Empty((await h.Filing.ListAsync(DisputeSide.Worker, WorkerId + 1)).Data!);

        Assert.Equal(id, (await h.Filing.GetAsync(DisputeSide.Customer, CustomerId, id)).Data!.DisputeId);
        Assert.Equal(id, (await h.Filing.GetAsync(DisputeSide.Worker, WorkerId, id)).Data!.DisputeId);
        Assert.Equal(404, (await h.Filing.GetAsync(DisputeSide.Customer, CustomerId + 1, id)).StatusCode);
        Assert.Equal(404, (await h.Filing.GetAsync(DisputeSide.Worker, WorkerId + 1, id)).StatusCode);
        Assert.Equal(404, (await h.Filing.GetAsync(DisputeSide.Customer, CustomerId, id + 99)).StatusCode);

        Assert.DoesNotContain(typeof(DisputeDto).GetProperties(), p => p.Name == "ResolvedBy"); // only AdminDisputeDto has it
        Assert.Equal(77, DisputeMapper.ToAdminDto(h.Repo.Tickets[0]).ResolvedBy);
    }

    // ---- priority -------------------------------------------------------------------------------

    private static DisputeTicket TicketDue(DateTime due, string status = "OPEN") => new()
    {
        DisputeId = 1,
        OrderId = OrderId,
        RaisedBy = "CUSTOMER",
        Category = "QUALITY",
        Description = "d",
        DisputeStatus = status,
        SlaDueAt = due,
        CreatedAt = Now,
    };

    [Theory]
    [InlineData(-3 * 3600, "HIGH")]      // overdue
    [InlineData(0, "HIGH")]
    [InlineData(6 * 3600 - 1, "HIGH")]
    [InlineData(6 * 3600, "MEDIUM")]     // exactly 6 h left is no longer "under 6 h"
    [InlineData(24 * 3600 - 1, "MEDIUM")]
    [InlineData(24 * 3600, "LOW")]
    [InlineData(47 * 3600, "LOW")]
    public void Priority_follows_the_time_left_with_the_6h_and_24h_bands(int secondsLeft, string expected)
    {
        Assert.Equal(expected, DisputeMapper.PriorityOf(TicketDue(Now.AddSeconds(secondsLeft)), Now, new DisputeOptions()));
    }

    [Fact]
    public void A_decided_ticket_is_LOW_with_no_time_remaining_and_an_overdue_one_is_negative()
    {
        Assert.Equal("LOW", DisputeMapper.PriorityOf(TicketDue(Now.AddMinutes(1), "RESOLVED"), Now, new DisputeOptions()));
        Assert.Equal(0, DisputeMapper.SecondsRemaining(TicketDue(Now.AddMinutes(1), "DISMISSED"), Now));
        Assert.Equal(-1800, DisputeMapper.SecondsRemaining(TicketDue(Now.AddMinutes(-30)), Now));
        Assert.Equal(3600, DisputeMapper.SecondsRemaining(TicketDue(Now.AddHours(1), "IN_REVIEW"), Now));
    }

    // ---- admin queue ----------------------------------------------------------------------------

    private static async Task<Harness> WithTicketsAsync(params (string status, double hoursLeft)[] tickets)
    {
        var h = new Harness();
        var id = 1;
        foreach (var (status, hoursLeft) in tickets)
        {
            h.Repo.Orders[1000 + id] = new DisputeOrderInfo(1000 + id, $"ORD{1000 + id}", CustomerId, []);
            h.Repo.Tickets.Add(new DisputeTicket
            {
                DisputeId = id,
                OrderId = 1000 + id,
                RaisedBy = "CUSTOMER",
                Category = id == 2 ? "ABSENT_FEE" : "QUALITY",
                Description = "d",
                DisputeStatus = status,
                SlaDueAt = Now.AddHours(hoursLeft),
                CreatedAt = Now,
            });
            h.Repo.Summaries[1000 + id] = new DisputeSummaryData(1000 + id, $"ORD{1000 + id}", "Chị Ngọc Anh",
                [new DisputeWorkerData(WorkerId, "Lý Văn Phúc", WorkerType.AgencyStaff, "CleanPro")]);
            id++;
        }

        await Task.CompletedTask;
        return h;
    }

    [Fact]
    public async Task The_default_queue_is_OPEN_and_IN_REVIEW_oldest_SLA_first_page_1_size_20()
    {
        var h = await WithTicketsAsync(("OPEN", 30), ("IN_REVIEW", 2), ("RESOLVED", 1), ("OPEN", 10));
        var result = await h.Admin.SearchAsync(null, null, null, null, null);

        Assert.True(result.Success);
        Assert.Equal(["OPEN", "IN_REVIEW"], h.Repo.LastFilter!.Statuses.ToArray());
        Assert.Equal(1, h.Repo.LastPage);
        Assert.Equal(20, h.Repo.LastPageSize);
        Assert.Equal([2, 4, 1], result.Data!.Items.Select(i => i.DisputeId).ToArray()); // by sla_due_at ascending, RESOLVED left out
        Assert.Equal(3, result.Data.Total);
    }

    [Fact]
    public async Task Summaries_carry_names_workers_priority_remaining_seconds_and_the_absent_fee_flag()
    {
        var h = await WithTicketsAsync(("OPEN", 3), ("OPEN", 30));
        var items = (await h.Admin.SearchAsync(null, null, null, null, null)).Data!.Items;

        var first = items[0];
        Assert.Equal("ORD1001", first.OrderCode);
        Assert.Equal("Chị Ngọc Anh", first.CustomerName);
        var worker = Assert.Single(first.Workers);
        Assert.Equal("AGENCY_STAFF", worker.WorkerType);
        Assert.Equal("CleanPro", worker.AgencyName);
        Assert.Equal("HIGH", first.Priority);
        Assert.Equal(3 * 3600, first.SlaSecondsRemaining);
        Assert.False(first.AutoCancelled);

        Assert.Equal("LOW", items[1].Priority);
        Assert.True(items[1].AutoCancelled); // ticket 2 is the ABSENT_FEE one
    }

    [Theory]
    [InlineData("HIGH", 6, null)]
    [InlineData("MEDIUM", 24, 6)]
    [InlineData("LOW", null, 24)]
    public async Task The_priority_filter_becomes_a_due_date_window_on_unresolved_tickets(string priority, int? beforeHours, int? fromHours)
    {
        var h = await WithTicketsAsync(("OPEN", 1));
        await h.Admin.SearchAsync("OPEN,IN_REVIEW,RESOLVED", priority, null, null, null);

        Assert.Equal(["OPEN", "IN_REVIEW"], h.Repo.LastFilter!.Statuses.ToArray()); // a priority never includes decided tickets
        Assert.Equal(beforeHours is { } b ? Now.AddHours(b) : null, h.Repo.LastFilter.DueBeforeUtc);
        Assert.Equal(fromHours is { } f ? Now.AddHours(f) : null, h.Repo.LastFilter.DueFromUtc);
    }

    [Fact]
    public async Task NearSla_keeps_unresolved_tickets_due_within_6_hours_overdue_included()
    {
        var h = await WithTicketsAsync(("OPEN", -5), ("OPEN", 5), ("IN_REVIEW", 7), ("RESOLVED", 1));
        var result = await h.Admin.SearchAsync("OPEN,IN_REVIEW,RESOLVED", null, "true", null, null);

        Assert.Equal(Now.AddHours(6), h.Repo.LastFilter!.DueBeforeUtc);
        Assert.Equal([1, 2], result.Data!.Items.Select(i => i.DisputeId).ToArray());
        Assert.Equal(2, result.Data.Total);
        Assert.Equal(-5 * 3600, result.Data.Items[0].SlaSecondsRemaining); // overdue shows as negative

        var both = await h.Admin.SearchAsync(null, "HIGH", "true", null, null);
        Assert.Equal(Now.AddHours(6), h.Repo.LastFilter.DueBeforeUtc); // the narrower bound wins
        Assert.True(both.Success);
    }

    [Fact]
    public async Task A_multi_status_list_is_trimmed_case_insensitive_and_de_duplicated()
    {
        var h = await WithTicketsAsync(("RESOLVED", 5));
        await h.Admin.SearchAsync(" resolved , Dismissed,RESOLVED", null, null, "2", "5");
        Assert.Equal(["RESOLVED", "DISMISSED"], h.Repo.LastFilter!.Statuses.ToArray());
        Assert.Equal(2, h.Repo.LastPage);
        Assert.Equal(5, h.Repo.LastPageSize);
    }

    [Theory]
    [InlineData("OPEN,NOPE", null, null, null, null, "status")]
    [InlineData(null, "URGENT", null, null, null, "priority")]
    [InlineData(null, null, "maybe", null, null, "nearSla")]
    [InlineData(null, null, null, "0", null, "page")]
    [InlineData(null, null, null, "x", null, "page")]
    [InlineData(null, null, null, null, "0", "pageSize")]
    [InlineData(null, null, null, null, "101", "pageSize")]
    public async Task Bad_query_values_are_a_400_naming_the_field_and_the_repository_is_not_called(
        string? status, string? priority, string? near, string? page, string? size, string field)
    {
        var h = await WithTicketsAsync(("OPEN", 1));
        var result = await h.Admin.SearchAsync(status, priority, near, page, size);

        Assert.Equal(400, result.StatusCode);
        Assert.Contains(field, result.ValidationErrors!.Keys);
        Assert.Null(h.Repo.LastFilter);
    }

    [Fact]
    public async Task A_page_size_of_exactly_100_is_accepted()
    {
        var h = await WithTicketsAsync(("OPEN", 1));
        Assert.True((await h.Admin.SearchAsync(null, null, null, null, "100")).Success);
        Assert.Equal(100, h.Repo.LastPageSize);
    }

    // ---- case file ------------------------------------------------------------------------------

    [Fact]
    public async Task The_case_file_has_the_ticket_the_summary_a_sorted_timeline_the_photos_and_no_checklist()
    {
        var h = await WithTicketsAsync(("OPEN", 20));
        h.Repo.Tickets[0].ResolvedBy = 5;
        h.Repo.Case = new DisputeCaseData(
            [new DisputeCheckInData(1, Now.AddHours(-6), true, 50.5m)],
            [
                new DisputePhotoData(1, "AFTER", 2, "/a2.jpg", 82, true, Now.AddHours(-2)),
                new DisputePhotoData(1, "BEFORE", 1, "/b1.jpg", 90, true, Now.AddHours(-5)),
                new DisputePhotoData(1, "AFTER", 1, "/a1.jpg", 40, false, Now.AddHours(-3)),
            ],
            [(1, Now.AddHours(-1))]);

        var file = (await h.Admin.GetAsync(1)).Data!;

        Assert.Equal(1, file.Dispute.DisputeId);
        Assert.Equal(5, file.Dispute.ResolvedBy);
        Assert.Equal("ORD1001", file.Summary.OrderCode);
        Assert.Null(file.Checklist);
        Assert.Equal(["CHECK_IN", "PHOTO_AFTER", "PHOTO_AFTER", "CHECK_OUT", "CUSTOMER_DISPUTED"], file.ShiftTimeline.Select(e => e.Type).ToArray());
        Assert.True(file.ShiftTimeline.Zip(file.ShiftTimeline.Skip(1)).All(p => p.First.At <= p.Second.At));
        Assert.Equal(50.5m, file.ShiftTimeline[0].DistanceM);
        Assert.True(file.ShiftTimeline[0].GpsVerified);
        Assert.Contains("50.5 m", file.ShiftTimeline[0].Detail); // invariant culture, never "50,5"
        Assert.Equal(40, file.ShiftTimeline[1].VolScore);
        Assert.Equal(["AFTER/1", "AFTER/2", "BEFORE/1"], file.Photos.Select(p => $"{p.Phase}/{p.AngleNo}").ToArray());
    }

    [Fact]
    public async Task An_unknown_ticket_has_no_case_file()
    {
        var h = await WithTicketsAsync(("OPEN", 1));
        Assert.Equal(404, (await h.Admin.GetAsync(999)).StatusCode);
    }

    // ---- take -----------------------------------------------------------------------------------

    [Fact]
    public async Task Taking_an_open_ticket_makes_it_IN_REVIEW_records_the_admin_and_saves_once()
    {
        var h = await WithTicketsAsync(("OPEN", 20));
        var result = await h.Admin.TakeAsync(42, 1);

        Assert.True(result.Success);
        Assert.Equal("IN_REVIEW", result.Data!.DisputeStatus);
        Assert.Equal(42, result.Data.ResolvedBy);
        Assert.Equal("IN_REVIEW", h.Repo.Tickets[0].DisputeStatus);
        Assert.Equal(1, h.Repo.Saves);
    }

    [Theory]
    [InlineData("IN_REVIEW")]
    [InlineData("RESOLVED")]
    [InlineData("DISMISSED")]
    public async Task A_ticket_that_is_not_open_cannot_be_taken_and_nothing_changes(string status)
    {
        var h = await WithTicketsAsync((status, 20));
        h.Repo.Tickets[0].ResolvedBy = 9;
        var result = await h.Admin.TakeAsync(42, 1);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(status, h.Repo.Tickets[0].DisputeStatus);
        Assert.Equal(9, h.Repo.Tickets[0].ResolvedBy);
        Assert.Equal(0, h.Repo.Saves);
    }

    [Fact]
    public async Task Taking_a_missing_ticket_is_404()
    {
        var h = await WithTicketsAsync(("OPEN", 20));
        Assert.Equal(404, (await h.Admin.TakeAsync(42, 999)).StatusCode);
    }
}
