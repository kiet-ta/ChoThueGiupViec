using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Services;
using MediatR;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Dispatch;

/// <summary>DISPATCH-WIRE-02 (#311): the worker endpoints of contract dispatch.md 3.1, on in-memory persistence.</summary>
public class WorkerDispatchServiceTests
{
    private static readonly DateTime StartTime = new(2026, 10, 15, 0, 30, 0, DateTimeKind.Utc);
    private static readonly DateOnly ShiftDate = new(2026, 10, 15);
    private static readonly GeoPoint Home = new(21.028511, 105.804817);

    private sealed class NoopMediator : IMediator
    {
        public List<object> Published { get; } = [];
        public Task Publish(object notification, CancellationToken cancellationToken = default) { Published.Add(notification); return Task.CompletedTask; }
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification { Published.Add(notification!); return Task.CompletedTask; }
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class TestUser : ICurrentUser
    {
        public int? UserId { get; set; }
        public UserRole? Role { get; set; } = UserRole.Worker;
        public bool IsAuthenticated => UserId != null;
    }

    /// <summary>Field persistence over the same assignments as <see cref="InMemoryDispatchRepository"/>, one log per assignment.</summary>
    private sealed class InMemoryFieldRepository(InMemoryDispatchRepository dispatch) : IFieldCheckInRepository, ICustomerAbsentRepository, IWorkerDispatchQuery
    {
        private long _nextLogId = 1;
        public Dictionary<long, CheckInLog> Logs { get; } = new();
        public Dictionary<int, GeoPoint> WorkerLocations { get; } = new();

        public Task<JobAssignment?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            dispatch.GetAssignmentByIdAsync(assignmentId, cancellationToken);

        public Task<CheckInLog?> GetCheckInLogAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Logs.TryGetValue(assignmentId, out var log) ? log : null);

        public Task<CheckInLog> SaveCheckInLogAsync(CheckInLog log, CancellationToken cancellationToken = default)
        {
            if (log.CheckinId == 0)
            {
                Assert.False(Logs.ContainsKey(log.AssignmentId), "CHECK_IN_LOG.assignment_id is unique: a second insert would fail");
                log.CheckinId = _nextLogId++;
            }

            Logs[log.AssignmentId] = log;
            return Task.FromResult(log);
        }

        public Task<bool> TransitionToCheckedInAsync(long assignmentId, DateTime checkedInAtUtc, CancellationToken cancellationToken = default)
        {
            var a = dispatch.Assignments[assignmentId];
            if (a.AssignmentStatus != JobAssignmentStatus.Assigned)
            {
                return Task.FromResult(false);
            }

            a.TransitionTo(JobAssignmentStatus.CheckedIn);
            return Task.FromResult(true);
        }

        public Task<bool> RecordCallAttemptAsync(long assignmentId, CancellationToken cancellationToken = default)
        {
            if (!Logs.TryGetValue(assignmentId, out var log))
            {
                return Task.FromResult(false);
            }

            log.CallAttempts++;
            return Task.FromResult(true);
        }

        public Task<bool> TryMarkCustomerAbsentAsync(long assignmentId, DateTime reportedAtUtc, CancellationToken cancellationToken = default)
        {
            if (Logs.TryGetValue(assignmentId, out var log) && log.CustomerAbsentAt == null)
            {
                log.CustomerAbsentAt = reportedAtUtc;
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public Task<bool> UpdateAssignmentAbsentFeeAsync(long assignmentId, decimal absenceFeeAmount, CancellationToken cancellationToken = default)
        {
            dispatch.Assignments[assignmentId].AbsenceFeeAmount = absenceFeeAmount;
            return Task.FromResult(true);
        }

        public async Task<OfferView?> GetOfferViewAsync(long assignmentId, CancellationToken cancellationToken = default)
        {
            var a = await dispatch.GetAssignmentByIdAsync(assignmentId, cancellationToken);
            return a == null
                ? null
                : new OfferView(a, ShiftDate, new TimeOnly(8, 0), new TimeOnly(12, 0), "Quận Cầu Giấy", Home,
                    WorkerLocations.TryGetValue(a.WorkerId, out var at) ? at : null);
        }

        public Task<GeoPoint?> GetOrderLocationOfAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<GeoPoint?>(dispatch.Assignments.ContainsKey(assignmentId) ? Home : null);
    }

    private sealed class Harness
    {
        public FakeClock Clock { get; } = new();
        public FakeWorkerAvailabilityQuery Query { get; } = new();
        public InMemoryDispatchRepository Repo { get; } = new();
        public InMemoryFieldRepository Field { get; }
        public TestUser User { get; } = new();
        public NoopMediator Mediator { get; } = new();
        public DispatchOfferEngine Engine { get; }
        public WorkerDispatchService Service { get; }

        public Harness()
        {
            Clock.Set(StartTime);
            Field = new InMemoryFieldRepository(Repo);
            var rules = MicrosoftOptions.Create(new BusinessRules());
            var geo = new HaversineGeoService();
            Engine = new DispatchOfferEngine(new SteppedRadiusDispatchScanner(Query, rules), Repo, Clock, Mediator, rules);
            Service = new WorkerDispatchService(
                Engine,
                new FieldCheckInService(Field, geo, Clock, rules),
                new CustomerAbsentService(Field, Clock, Mediator, rules),
                Repo, Field, Field, geo, User, Clock);
        }

        public void AddWorker(int workerId, int slotId, double distanceKm)
        {
            Query.Workers.Add(new AvailableWorker(workerId, slotId, distanceKm));
            Repo.AddSlot(slotId, workerId);
        }

        public async Task<DispatchOffer> OfferAsync(long orderId)
        {
            Repo.Orders[orderId] = new DispatchOrderContext(orderId, 1, ServiceTier.Economy, ShiftDate, "SANG", 1, Home, 260000m);
            return (await Engine.StartDispatchForOrderAsync(orderId, 1, ServiceTier.Economy, ShiftDate, "SANG", Home, 260000m))!;
        }

        /// <summary>An assigned job of <paramref name="workerId"/> (optionally an Agency Staff job).</summary>
        public async Task<long> AssignedAsync(long orderId, int workerId, int slotId, int? agencyId = null)
        {
            AddWorker(workerId, slotId, 1.0);
            var offer = await OfferAsync(orderId);
            User.UserId = workerId;
            await Service.AcceptAsync(offer.AssignmentId);
            Repo.Assignments[offer.AssignmentId].AgencyId = agencyId;
            return offer.AssignmentId;
        }
    }

    [Fact]
    public async Task Current_offer_is_shaped_as_the_contract_JobOfferDto()
    {
        var h = new Harness();
        h.AddWorker(42, slotId: 7, distanceKm: 3.0);
        h.Field.WorkerLocations[42] = new GeoPoint(21.0285, 105.8542);
        var offer = await h.OfferAsync(501);
        h.User.UserId = 42;
        h.Clock.Advance(TimeSpan.FromSeconds(2));

        var dto = await h.Service.GetCurrentOfferAsync();

        Assert.NotNull(dto);
        Assert.Equal(offer.AssignmentId, dto.AssignmentId);
        Assert.Equal("ECONOMY", dto.ServiceTier);
        Assert.Equal("08:00-12:00", dto.ShiftTime);
        Assert.Equal(4.0, dto.EstimatedDurationHours);
        Assert.Equal("Quận Cầu Giấy", dto.District);
        Assert.InRange(dto.ApproximateDistanceKm!.Value, 4.0, 6.0); // Haversine between the two Hanoi points (~5 km)
        Assert.Equal(208000m, dto.NetEarnings);
        Assert.Equal(28, dto.RemainingSeconds);

        h.User.UserId = 43; // another worker sees nothing
        Assert.Null(await h.Service.GetCurrentOfferAsync());
    }

    [Fact]
    public async Task Accept_of_someone_elses_offer_is_404_and_an_expired_one_is_409_OFFER_EXPIRED()
    {
        var h = new Harness();
        h.AddWorker(42, slotId: 7, distanceKm: 1.0);
        var offer = await h.OfferAsync(502);

        h.User.UserId = 99;
        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.AcceptAsync(offer.AssignmentId));

        h.User.UserId = 42;
        h.Clock.Advance(TimeSpan.FromSeconds(31));
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.AcceptAsync(offer.AssignmentId));
        Assert.Equal(DispatchErrorCodes.OfferExpired, ex.Code);
    }

    [Fact]
    public async Task Accept_then_decline_again_is_409_OFFER_NOT_OPEN()
    {
        var h = new Harness();
        h.AddWorker(42, slotId: 7, distanceKm: 1.0);
        var offer = await h.OfferAsync(503);
        h.User.UserId = 42;

        var accepted = await h.Service.AcceptAsync(offer.AssignmentId);
        Assert.Equal("ASSIGNED", accepted.Status);
        Assert.True(accepted.BookingSlotLocked);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.DeclineAsync(offer.AssignmentId, "late"));
        Assert.Equal(DispatchErrorCodes.OfferNotOpen, ex.Code);
    }

    [Fact]
    public async Task Decline_answers_DECLINED_and_offers_the_next_worker()
    {
        var h = new Harness();
        h.AddWorker(42, slotId: 7, distanceKm: 1.0);
        h.AddWorker(43, slotId: 8, distanceKm: 2.0);
        var offer = await h.OfferAsync(504);
        h.User.UserId = 42;

        var declined = await h.Service.DeclineAsync(offer.AssignmentId, "Quá xa");

        Assert.Equal("DECLINED", declined.Status);
        h.User.UserId = 43;
        Assert.Equal(504, (await h.Service.GetCurrentOfferAsync())!.OrderId);
    }

    [Fact]
    public async Task Check_in_rejects_bad_coordinates_and_is_409_unless_ASSIGNED()
    {
        var h = new Harness();
        h.AddWorker(42, slotId: 7, distanceKm: 1.0);
        var offer = await h.OfferAsync(505);
        h.User.UserId = 42;

        await Assert.ThrowsAsync<ArgumentException>(() => h.Service.CheckInAsync(offer.AssignmentId, 91, 0));
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.CheckInAsync(offer.AssignmentId, Home.Latitude, Home.Longitude));
        Assert.Equal(DispatchErrorCodes.InvalidAssignmentState, ex.Code); // still OFFERED
    }

    [Fact]
    public async Task Check_in_far_away_needs_an_alternative_the_retry_reuses_the_log_and_a_plate_photo_completes_it()
    {
        var h = new Harness();
        var id = await h.AssignedAsync(506, workerId: 42, slotId: 7);

        var far = await h.Service.CheckInAsync(id, 21.0400, 105.8000); // ~1.3 km away
        Assert.False(far.IsGpsVerified);
        Assert.True(far.RequiresAlternativeVerification);
        Assert.Equal(JobAssignmentStatus.Assigned, h.Repo.Assignments[id].AssignmentStatus);

        var stillFar = await h.Service.CheckInAsync(id, 21.0390, 105.8010);
        Assert.Equal(far.CheckInId, stillFar.CheckInId); // same CHECK_IN_LOG row
        Assert.Single(h.Field.Logs);

        var plate = await h.Service.SubmitPlatePhotoAsync(id, "https://storage.local/plates/506.jpg");
        Assert.Equal("PLATE_PHOTO", plate.VerificationMethod);
        Assert.Equal("https://storage.local/plates/506.jpg", plate.PlatePhotoUrl);
        Assert.Equal(JobAssignmentStatus.CheckedIn, h.Repo.Assignments[id].AssignmentStatus);
    }

    [Fact]
    public async Task Calls_and_absent_report_follow_BR05_and_a_second_report_is_409()
    {
        var h = new Harness();
        var id = await h.AssignedAsync(507, workerId: 42, slotId: 7);
        var checkIn = await h.Service.CheckInAsync(id, Home.Latitude, Home.Longitude);
        Assert.True(checkIn.IsGpsVerified);
        Assert.Equal("GPS", checkIn.VerificationMethod);

        Assert.Equal(1, (await h.Service.LogCallAsync(id)).CallAttempts);
        h.Clock.Advance(TimeSpan.FromMinutes(16));
        var early = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.ReportAbsentAsync(id));
        Assert.Equal(DispatchErrorCodes.AbsentConditionsNotMet, early.Code); // only 1 call

        Assert.Equal(2, (await h.Service.LogCallAsync(id)).CallAttempts);
        var report = await h.Service.ReportAbsentAsync(id);
        Assert.Equal("PENDING_APPROVAL", report.ReviewStatus);
        Assert.Equal(104000m, report.AbsenceFeeAmount); // 40 % of 260 000 (Q10)

        var again = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.ReportAbsentAsync(id));
        Assert.Equal(DispatchErrorCodes.AbsentAlreadyReported, again.Code);
    }

    [Fact]
    public async Task Another_worker_of_the_same_agency_cannot_act_on_an_Agency_Staff_assignment()
    {
        var h = new Harness();
        var id = await h.AssignedAsync(508, workerId: 42, slotId: 7, agencyId: 5);
        h.User.UserId = 44; // colleague in agency 5

        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.CheckInAsync(id, Home.Latitude, Home.Longitude));
        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.LogCallAsync(id));
        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.ReportAbsentAsync(id));

        // The services themselves no longer skip the owner check when agency_id is set.
        var direct = await new FieldCheckInService(h.Field, new HaversineGeoService(), h.Clock, MicrosoftOptions.Create(new BusinessRules()))
            .CheckInAsync(id, 44, Home, Home);
        Assert.False(direct.Success);
        Assert.Equal(JobAssignmentStatus.Assigned, h.Repo.Assignments[id].AssignmentStatus);
    }

    [Fact]
    public async Task A_caller_who_is_not_a_worker_is_forbidden()
    {
        var h = new Harness();
        h.User.UserId = 1;
        h.User.Role = UserRole.Customer;

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => h.Service.GetCurrentOfferAsync());
    }

    [Fact]
    public async Task Plate_photo_check_in_starts_the_15_minute_wait_when_it_completes()
    {
        var h = new Harness();
        var id = await h.AssignedAsync(509, workerId: 42, slotId: 7);
        await h.Service.CheckInAsync(id, 21.0400, 105.8000); // GPS miss
        h.Clock.Advance(TimeSpan.FromMinutes(20)); // the worker waits before sending the photo

        var plate = await h.Service.SubmitPlatePhotoAsync(id, "https://storage.local/plates/509.jpg");
        Assert.Equal(h.Clock.UtcNow, plate.CheckedInAt);

        await h.Service.LogCallAsync(id);
        await h.Service.LogCallAsync(id);
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.ReportAbsentAsync(id));
        Assert.Equal(DispatchErrorCodes.AbsentConditionsNotMet, ex.Code); // 0 minutes since the completed check-in
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://storage.local/p.jpg")]
    public async Task Plate_photo_url_must_be_an_absolute_http_url(string url)
    {
        var h = new Harness();
        var id = await h.AssignedAsync(510, workerId: 42, slotId: 7);

        await Assert.ThrowsAsync<ArgumentException>(() => h.Service.SubmitPlatePhotoAsync(id, url));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Service.SubmitPlatePhotoAsync(id, "https://s/" + new string('a', 500)));
    }

    [Fact]
    public async Task A_check_in_from_very_far_away_is_stored_within_the_distance_column()
    {
        var h = new Harness();
        var id = await h.AssignedAsync(511, workerId: 42, slotId: 7);

        var result = await h.Service.CheckInAsync(id, 0, 0); // emulator default location, thousands of km away

        Assert.True(result.RequiresAlternativeVerification);
        Assert.Equal(99999.99m, result.DistanceMeters); // CHECK_IN_LOG.distance_m DECIMAL(7,2) maximum
    }

    [Fact]
    public async Task Absent_report_that_loses_the_race_is_ABSENT_ALREADY_REPORTED()
    {
        var h = new Harness();
        var id = await h.AssignedAsync(512, workerId: 42, slotId: 7);
        await h.Service.CheckInAsync(id, Home.Latitude, Home.Longitude);
        await h.Service.LogCallAsync(id);
        await h.Service.LogCallAsync(id);
        h.Clock.Advance(TimeSpan.FromMinutes(16));
        // Another request set customer_absent_at between this request's read and its write.
        var log = h.Field.Logs[id];
        var racing = new CustomerAbsentService(new RacingAbsentRepository(h.Field, log), h.Clock, h.Mediator,
            MicrosoftOptions.Create(new BusinessRules()));

        var result = await racing.ReportCustomerAbsentAsync(id, 42);

        Assert.False(result.Success);
        Assert.True(result.AlreadyReported);
        Assert.DoesNotContain(h.Mediator.Published, e => e is CommonService.Domain.Events.CustomerAbsentReported);
    }

    /// <summary>Returns the log as it was before a concurrent report, then refuses the conditional write.</summary>
    private sealed class RacingAbsentRepository(InMemoryFieldRepository inner, CheckInLog snapshot) : ICustomerAbsentRepository
    {
        public Task<JobAssignment?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default) => inner.GetAssignmentAsync(assignmentId, cancellationToken);
        public Task<CheckInLog?> GetCheckInLogAsync(long assignmentId, CancellationToken cancellationToken = default) => Task.FromResult<CheckInLog?>(snapshot);
        public Task<CheckInLog> SaveCheckInLogAsync(CheckInLog log, CancellationToken cancellationToken = default) => inner.SaveCheckInLogAsync(log, cancellationToken);
        public Task<bool> UpdateAssignmentAbsentFeeAsync(long assignmentId, decimal absenceFeeAmount, CancellationToken cancellationToken = default) => inner.UpdateAssignmentAbsentFeeAsync(assignmentId, absenceFeeAmount, cancellationToken);
        public Task<bool> RecordCallAttemptAsync(long assignmentId, CancellationToken cancellationToken = default) => inner.RecordCallAttemptAsync(assignmentId, cancellationToken);
        public Task<bool> TryMarkCustomerAbsentAsync(long assignmentId, DateTime reportedAtUtc, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
