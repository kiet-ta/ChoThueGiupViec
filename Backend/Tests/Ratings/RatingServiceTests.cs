using System.Text.Json;
using CommonService.Application.Common.Options;
using CommonService.Application.Features.Ratings;
using CommonService.Application.Features.Ratings.Dtos;
using CommonService.Application.Features.Ratings.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Options;

namespace CommonService.Tests.Ratings;

/// <summary>BE-M6-01a: two-way rating rules (contract ratings.md, decisions Q14 / SC-5) with an in-memory repository.</summary>
public class RatingServiceTests
{
    private static readonly DateTime CompletedAt = new(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc);
    private const int CustomerId = 11;
    private const int WorkerId = 22;
    private const long AssignmentId = 500;

    private sealed class TestClock(DateTime initialUtc) : IClock
    {
        public DateTime UtcNow { get; set; } = initialUtc;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => local.AddHours(-7);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(UtcNow));
    }

    private sealed class MemoryRatings : IRatingRepository
    {
        public Dictionary<long, RatingAssignmentInfo> Assignments { get; } = [];
        public List<TwoWayRating> Saved { get; } = [];
        public bool LoseTheRace { get; set; }
        private long _nextId = 1;

        public Task<RatingAssignmentInfo?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Assignments.GetValueOrDefault(assignmentId));

        public Task<bool> ExistsAsync(long assignmentId, string raterRole, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.Any(r => r.AssignmentId == assignmentId && r.RaterRole == raterRole));

        public Task<bool> TryAddAsync(TwoWayRating rating, CancellationToken cancellationToken = default)
        {
            if (LoseTheRace || Saved.Any(r => r.AssignmentId == rating.AssignmentId && r.RaterRole == rating.RaterRole))
            {
                return Task.FromResult(false);
            }

            rating.RatingId = _nextId++;
            Saved.Add(rating);
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingPublisher : IPublisher
    {
        public List<object> Published { get; } = [];
        public bool Throw { get; set; }

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Record(notification);

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Record(notification!);

        private Task Record(object notification)
        {
            if (Throw) throw new InvalidOperationException("consumer failed");
            Published.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class Harness
    {
        public MemoryRatings Repo { get; } = new();
        public TestClock Clock { get; } = new(CompletedAt.AddHours(1));
        public RecordingPublisher Publisher { get; } = new();
        public RatingService Service { get; }

        public Harness(JobAssignmentStatus status = JobAssignmentStatus.Completed, DateTime? completedAt = null)
        {
            Repo.Assignments[AssignmentId] = new RatingAssignmentInfo(
                AssignmentId, CustomerId, WorkerId, status,
                status == JobAssignmentStatus.Completed ? completedAt ?? CompletedAt : completedAt);
            Service = new RatingService(Repo, Clock, Microsoft.Extensions.Options.Options.Create(new BusinessRules()), Publisher);
        }
    }

    private static SubmitRatingRequestDto CustomerRequest(int stars = 5, string? comment = null) => new()
    {
        Stars = stars,
        Criteria = new Dictionary<string, int> { ["punctuality"] = 5, ["cleaningQuality"] = 4, ["attitude"] = 3 },
        Comment = comment,
    };

    private static SubmitRatingRequestDto WorkerRequest(int stars = 4) => new()
    {
        Stars = stars,
        Criteria = new Dictionary<string, int> { ["cooperation"] = 5, ["workingConditions"] = 2 },
    };

    // ---- window ---------------------------------------------------------------------------------

    [Fact]
    public async Task Window_is_NOT_COMPLETED_until_the_assignment_is_COMPLETED()
    {
        var h = new Harness(JobAssignmentStatus.InProgress);
        var result = await h.Service.GetWindowAsync(RaterSide.Customer, CustomerId, AssignmentId);

        var window = result.Data!;
        Assert.False(window.CanRate);
        Assert.Equal("NOT_COMPLETED", window.Reason);
        Assert.Null(window.OpensAt);
        Assert.Null(window.ClosesAt);
        Assert.Equal(0, window.SecondsRemaining);
    }

    [Fact]
    public async Task Window_is_OPEN_for_48_hours_after_completed_at_and_counts_the_seconds_left()
    {
        var h = new Harness();
        var window = (await h.Service.GetWindowAsync(RaterSide.Customer, CustomerId, AssignmentId)).Data!;

        Assert.True(window.CanRate);
        Assert.Equal("OPEN", window.Reason);
        Assert.Equal(CompletedAt, window.OpensAt);
        Assert.Equal(CompletedAt.AddHours(48), window.ClosesAt);
        Assert.Equal(47 * 3600, window.SecondsRemaining); // the clock is one hour after completion
    }

    [Fact]
    public async Task The_exact_closing_instant_is_still_open_and_one_tick_later_it_is_closed()
    {
        var h = new Harness();
        h.Clock.UtcNow = CompletedAt.AddHours(48);
        Assert.Equal("OPEN", (await h.Service.GetWindowAsync(RaterSide.Customer, CustomerId, AssignmentId)).Data!.Reason);

        h.Clock.UtcNow = CompletedAt.AddHours(48).AddTicks(1);
        var closed = (await h.Service.GetWindowAsync(RaterSide.Customer, CustomerId, AssignmentId)).Data!;
        Assert.Equal("WINDOW_CLOSED", closed.Reason);
        Assert.False(closed.CanRate);
        Assert.Equal(0, closed.SecondsRemaining);
        Assert.Equal(CompletedAt.AddHours(48), closed.ClosesAt);
    }

    [Fact]
    public async Task Window_reports_ALREADY_RATED_per_side()
    {
        var h = new Harness();
        Assert.Equal(201, (await h.Service.SubmitAsync(RaterSide.Customer, CustomerId, AssignmentId, CustomerRequest())).StatusCode);

        Assert.Equal("ALREADY_RATED", (await h.Service.GetWindowAsync(RaterSide.Customer, CustomerId, AssignmentId)).Data!.Reason);
        // the worker has not rated yet: the two directions are independent (one rating per assignment AND rater role)
        Assert.Equal("OPEN", (await h.Service.GetWindowAsync(RaterSide.Worker, WorkerId, AssignmentId)).Data!.Reason);
    }

    [Fact]
    public async Task Window_of_another_callers_or_a_missing_assignment_is_404_never_a_reason()
    {
        var h = new Harness();
        Assert.Equal(404, (await h.Service.GetWindowAsync(RaterSide.Customer, CustomerId + 1, AssignmentId)).StatusCode);
        Assert.Equal(404, (await h.Service.GetWindowAsync(RaterSide.Worker, CustomerId, AssignmentId)).StatusCode); // a customer id is not the worker id
        Assert.Equal(404, (await h.Service.GetWindowAsync(RaterSide.Customer, CustomerId, AssignmentId + 1)).StatusCode);
        Assert.Null((await h.Service.GetWindowAsync(RaterSide.Customer, CustomerId + 1, AssignmentId)).Data);
    }

    // ---- submit: success ------------------------------------------------------------------------

    [Fact]
    public async Task Customer_rating_is_saved_with_the_workers_id_snake_case_criteria_and_returned_in_camelCase()
    {
        var h = new Harness();
        var result = await h.Service.SubmitAsync(RaterSide.Customer, CustomerId, AssignmentId, CustomerRequest(5, "  Rất tốt  "));

        Assert.True(result.Success);
        Assert.Equal(201, result.StatusCode);
        var saved = Assert.Single(h.Repo.Saved);
        Assert.Equal(AssignmentId, saved.AssignmentId);
        Assert.Equal(WorkerId, saved.WorkerId); // copied from the assignment
        Assert.Equal("CUSTOMER", saved.RaterRole);
        Assert.Equal((byte)5, saved.Stars);
        Assert.Equal("Rất tốt", saved.Comment);
        Assert.Equal(h.Clock.UtcNow, saved.CreatedAt);

        var stored = JsonSerializer.Deserialize<Dictionary<string, int>>(saved.CriteriaJson!)!;
        Assert.Equal(new Dictionary<string, int> { ["punctuality"] = 5, ["cleaning_quality"] = 4, ["attitude"] = 3 }, stored);

        var dto = result.Data!;
        Assert.Equal(saved.RatingId, dto.RatingId);
        Assert.Equal("CUSTOMER", dto.RaterRole);
        Assert.Equal(new Dictionary<string, int> { ["punctuality"] = 5, ["cleaningQuality"] = 4, ["attitude"] = 3 }, dto.Criteria);
    }

    [Fact]
    public async Task Worker_rating_stores_the_two_worker_criteria_and_the_worker_id_of_the_assignment()
    {
        var h = new Harness();
        var result = await h.Service.SubmitAsync(RaterSide.Worker, WorkerId, AssignmentId, WorkerRequest());

        Assert.Equal(201, result.StatusCode);
        var saved = Assert.Single(h.Repo.Saved);
        Assert.Equal("WORKER", saved.RaterRole);
        Assert.Equal(WorkerId, saved.WorkerId);
        var stored = JsonSerializer.Deserialize<Dictionary<string, int>>(saved.CriteriaJson!)!;
        Assert.Equal(new Dictionary<string, int> { ["cooperation"] = 5, ["working_conditions"] = 2 }, stored);
        Assert.Equal(new Dictionary<string, int> { ["cooperation"] = 5, ["workingConditions"] = 2 }, result.Data!.Criteria);
    }

    [Fact]
    public async Task Success_publishes_RatingSubmitted_once_after_saving()
    {
        var h = new Harness();
        await h.Service.SubmitAsync(RaterSide.Customer, CustomerId, AssignmentId, CustomerRequest(4));

        var published = Assert.IsType<RatingSubmitted>(Assert.Single(h.Publisher.Published));
        Assert.Equal(h.Repo.Saved[0].RatingId, published.RatingId);
        Assert.Equal(AssignmentId, published.AssignmentId);
        Assert.Equal("CUSTOMER", published.RaterRole);
        Assert.Equal(4, published.Stars);
        Assert.Equal(h.Clock.UtcNow, published.CreatedAtUtc);
    }

    [Fact]
    public async Task A_failing_event_consumer_does_not_turn_a_saved_rating_into_an_error()
    {
        var h = new Harness();
        h.Publisher.Throw = true;
        var result = await h.Service.SubmitAsync(RaterSide.Customer, CustomerId, AssignmentId, CustomerRequest());
        Assert.Equal(201, result.StatusCode);
        Assert.Single(h.Repo.Saved);
    }

    [Fact]
    public async Task A_comment_of_exactly_500_characters_and_a_blank_comment_are_accepted()
    {
        var h = new Harness();
        Assert.Equal(201, (await h.Service.SubmitAsync(RaterSide.Customer, CustomerId, AssignmentId, CustomerRequest(5, new string('a', 500)))).StatusCode);
        Assert.Equal(500, h.Repo.Saved[0].Comment!.Length);

        var worker = await h.Service.SubmitAsync(RaterSide.Worker, WorkerId, AssignmentId,
            new SubmitRatingRequestDto { Stars = 3, Criteria = WorkerRequest().Criteria, Comment = "   " });
        Assert.Equal(201, worker.StatusCode);
        Assert.Null(h.Repo.Saved[1].Comment);
    }

    [Fact]
    public async Task Submitting_at_the_exact_closing_instant_is_accepted()
    {
        var h = new Harness();
        h.Clock.UtcNow = CompletedAt.AddHours(48);
        Assert.Equal(201, (await h.Service.SubmitAsync(RaterSide.Customer, CustomerId, AssignmentId, CustomerRequest())).StatusCode);
    }

    // ---- submit: validation ---------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task Stars_must_be_1_to_5(int? stars)
    {
        var h = new Harness();
        var request = new SubmitRatingRequestDto { Stars = stars, Criteria = CustomerRequest().Criteria };
        var result = await h.Service.SubmitAsync(RaterSide.Customer, CustomerId, AssignmentId, request);

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("stars", result.ValidationErrors!.Keys);
        Assert.Empty(h.Repo.Saved);
    }

    [Fact]
    public async Task Missing_criteria_missing_keys_extra_keys_and_out_of_range_values_are_each_named()
    {
        var h = new Harness();

        var none = await h.Service.SubmitAsync(RaterSide.Customer, CustomerId, AssignmentId, new SubmitRatingRequestDto { Stars = 5 });
        Assert.Contains("criteria", none.ValidationErrors!.Keys);

        var partial = await h.Service.SubmitAsync(RaterSide.Customer, CustomerId, AssignmentId, new SubmitRatingRequestDto
        {
            Stars = 5,
            Criteria = new Dictionary<string, int> { ["punctuality"] = 0, ["attitude"] = 6, ["mood"] = 3 },
        });
        var keys = partial.ValidationErrors!.Keys.Order().ToArray();
        Assert.Equal(["criteria.attitude", "criteria.cleaningQuality", "criteria.mood", "criteria.punctuality"], keys);
        Assert.Empty(h.Repo.Saved);
    }

    [Fact]
    public async Task Each_side_accepts_only_its_own_criteria()
    {
        var h = new Harness();
        var customerWithWorkerKeys = await h.Service.SubmitAsync(RaterSide.Customer, CustomerId, AssignmentId, WorkerRequest());
        Assert.Equal(400, customerWithWorkerKeys.StatusCode);
        Assert.Contains("criteria.cooperation", customerWithWorkerKeys.ValidationErrors!.Keys);

        var workerWithCustomerKeys = await h.Service.SubmitAsync(RaterSide.Worker, WorkerId, AssignmentId, CustomerRequest());
        Assert.Equal(400, workerWithCustomerKeys.StatusCode);
        Assert.Contains("criteria.punctuality", workerWithCustomerKeys.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task Criteria_keys_are_exact_camelCase_not_snake_case()
    {
        var h = new Harness();
        var request = new SubmitRatingRequestDto
        {
            Stars = 5,
            Criteria = new Dictionary<string, int> { ["punctuality"] = 5, ["cleaning_quality"] = 4, ["attitude"] = 3 },
        };
        var result = await h.Service.SubmitAsync(RaterSide.Customer, CustomerId, AssignmentId, request);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("criteria.cleaningQuality", result.ValidationErrors!.Keys);
        Assert.Contains("criteria.cleaning_quality", result.ValidationErrors.Keys);
    }

    [Fact]
    public async Task A_501_character_comment_is_rejected()
    {
        var h = new Harness();
        var result = await h.Service.SubmitAsync(RaterSide.Customer, CustomerId, AssignmentId, CustomerRequest(5, new string('a', 501)));
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("comment", result.ValidationErrors!.Keys);
    }

    // ---- submit: ownership and state ------------------------------------------------------------

    [Fact]
    public async Task Another_callers_assignment_is_404_even_with_a_valid_body_and_nothing_is_saved()
    {
        var h = new Harness();
        var result = await h.Service.SubmitAsync(RaterSide.Customer, CustomerId + 1, AssignmentId, CustomerRequest());
        Assert.Equal(404, result.StatusCode);
        Assert.Empty(h.Repo.Saved);
        Assert.Empty(h.Publisher.Published);
    }

    [Fact]
    public async Task Not_completed_and_window_closed_are_409()
    {
        var notCompleted = new Harness(JobAssignmentStatus.AwaitingAcceptance);
        var a = await notCompleted.Service.SubmitAsync(RaterSide.Customer, CustomerId, AssignmentId, CustomerRequest());
        Assert.Equal(409, a.StatusCode);
        Assert.Contains("not completed", a.ErrorMessage);

        var closed = new Harness();
        closed.Clock.UtcNow = CompletedAt.AddHours(48).AddSeconds(1);
        var b = await closed.Service.SubmitAsync(RaterSide.Customer, CustomerId, AssignmentId, CustomerRequest());
        Assert.Equal(409, b.StatusCode);
        Assert.Contains("closed", b.ErrorMessage);
        Assert.Empty(closed.Repo.Saved);
    }

    [Fact]
    public async Task A_second_rating_of_the_same_role_is_409_and_publishes_nothing_more()
    {
        var h = new Harness();
        await h.Service.SubmitAsync(RaterSide.Customer, CustomerId, AssignmentId, CustomerRequest());
        var again = await h.Service.SubmitAsync(RaterSide.Customer, CustomerId, AssignmentId, CustomerRequest(1));

        Assert.Equal(409, again.StatusCode);
        Assert.Contains("already", again.ErrorMessage);
        Assert.Single(h.Repo.Saved);
        Assert.Single(h.Publisher.Published);
    }

    [Fact]
    public async Task Losing_the_race_on_the_unique_index_is_409_ALREADY_RATED_and_publishes_nothing()
    {
        var h = new Harness();
        h.Repo.LoseTheRace = true; // the existence check says "free", the insert finds a duplicate
        var result = await h.Service.SubmitAsync(RaterSide.Customer, CustomerId, AssignmentId, CustomerRequest());
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("already", result.ErrorMessage);
        Assert.Empty(h.Publisher.Published);
    }

    // ---- internal-only (Q14) --------------------------------------------------------------------

    [Fact]
    public void The_service_can_only_read_a_window_and_submit_nothing_returns_a_worker_to_customer_rating()
    {
        var methods = typeof(IRatingService).GetMethods().Select(m => m.Name).Order().ToArray();
        Assert.Equal(["GetWindowAsync", "SubmitAsync"], methods);
    }

    [Fact]
    public async Task The_response_to_a_worker_rating_goes_to_the_worker_who_wrote_it()
    {
        var h = new Harness();
        var result = await h.Service.SubmitAsync(RaterSide.Worker, WorkerId, AssignmentId, WorkerRequest());
        Assert.Equal("WORKER", result.Data!.RaterRole);
        // a customer cannot ask for it: the customer-side call for the same assignment still shows its own state
        var customerView = await h.Service.GetWindowAsync(RaterSide.Customer, CustomerId, AssignmentId);
        Assert.Equal("OPEN", customerView.Data!.Reason);
    }
}
