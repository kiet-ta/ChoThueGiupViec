using CommonService.Application.Common.Options;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Dispatch;

public class FieldCheckInServiceTests
{
    private sealed class InMemoryFieldCheckInRepository : IFieldCheckInRepository
    {
        private readonly Dictionary<long, JobAssignment> _assignments = new();
        private readonly Dictionary<long, CheckInLog> _logs = new();
        private long _checkinCounter = 1;

        public void AddAssignment(JobAssignment assignment) => _assignments[assignment.AssignmentId] = assignment;

        public Task<JobAssignment?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default)
        {
            _assignments.TryGetValue(assignmentId, out var a);
            return Task.FromResult(a);
        }

        public Task<CheckInLog?> GetCheckInLogAsync(long assignmentId, CancellationToken cancellationToken = default)
        {
            _logs.TryGetValue(assignmentId, out var l);
            return Task.FromResult(l);
        }

        public Task<CheckInLog> SaveCheckInLogAsync(CheckInLog log, CancellationToken cancellationToken = default)
        {
            if (log.CheckinId == 0)
                log.CheckinId = _checkinCounter++;
            _logs[log.AssignmentId] = log;
            return Task.FromResult(log);
        }

        public Task<bool> TransitionToCheckedInAsync(long assignmentId, DateTime checkedInAtUtc, CancellationToken cancellationToken = default)
        {
            if (_assignments.TryGetValue(assignmentId, out var assignment))
            {
                if (assignment.AssignmentStatus == JobAssignmentStatus.Assigned)
                {
                    assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
                    return Task.FromResult(true);
                }
            }
            return Task.FromResult(false);
        }
    }

    [Fact]
    public async Task CheckIn_within_100m_succeeds_and_transitions_to_CHECKED_IN()
    {
        var now = new DateTime(2026, 10, 15, 7, 55, 0, DateTimeKind.Utc);
        var clock = new FakeClock();
        clock.Set(now);

        var repo = new InMemoryFieldCheckInRepository();
        var assignment = new JobAssignment
        {
            AssignmentId = 101,
            OrderId = 1,
            CustomerId = 10,
            WorkerId = 20,
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now.AddHours(-1)
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        repo.AddAssignment(assignment);

        var geoService = new FakeGeoService(); // Distance will be configured
        // Target: 10.762622, 106.660172
        var target = new GeoPoint(10.762622, 106.660172);
        // Worker device close by: within 45 meters
        var device = new GeoPoint(10.762900, 106.660200);

        var service = new FieldCheckInService(
            repo,
            geoService,
            clock,
            MicrosoftOptions.Create(new BusinessRules())
        );

        var result = await service.CheckInAsync(101, 20, device, target);

        Assert.True(result.Success);
        Assert.True(result.IsGpsVerified);
        Assert.False(result.RequiresFallback);
        Assert.Equal(JobAssignmentStatus.CheckedIn, result.Status);

        var updatedAssignment = await repo.GetAssignmentAsync(101);
        Assert.Equal(JobAssignmentStatus.CheckedIn, updatedAssignment!.AssignmentStatus);

        var log = await repo.GetCheckInLogAsync(101);
        Assert.NotNull(log);
        Assert.True(log.GpsVerified);
        Assert.True(log.DistanceM <= 100m);
    }

    [Fact]
    public async Task CheckIn_deviation_over_100m_requires_fallback_and_stays_in_ASSIGNED()
    {
        var now = new DateTime(2026, 10, 15, 7, 55, 0, DateTimeKind.Utc);
        var clock = new FakeClock();
        clock.Set(now);

        var repo = new InMemoryFieldCheckInRepository();
        var assignment = new JobAssignment
        {
            AssignmentId = 102,
            OrderId = 2,
            CustomerId = 11,
            WorkerId = 21,
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now.AddHours(-1)
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        repo.AddAssignment(assignment);

        var geoService = new FakeGeoService();
        // Target: 10.762622, 106.660172
        var target = new GeoPoint(10.762622, 106.660172);
        // Worker device ~350 meters away
        var device = new GeoPoint(10.765500, 106.662000);

        var service = new FieldCheckInService(
            repo,
            geoService,
            clock,
            MicrosoftOptions.Create(new BusinessRules())
        );

        var result = await service.CheckInAsync(102, 21, device, target);

        Assert.False(result.Success);
        Assert.False(result.IsGpsVerified);
        Assert.True(result.RequiresFallback);
        Assert.Equal(JobAssignmentStatus.Assigned, result.Status);
        Assert.Contains("lệch", result.ErrorMessage);

        var updatedAssignment = await repo.GetAssignmentAsync(102);
        Assert.Equal(JobAssignmentStatus.Assigned, updatedAssignment!.AssignmentStatus);

        var log = await repo.GetCheckInLogAsync(102);
        Assert.NotNull(log);
        Assert.False(log.GpsVerified);
        Assert.True(log.DistanceM > 100m);
    }

    [Fact]
    public async Task Fallback_confirmation_via_PLATE_PHOTO_or_CUSTOMER_CONFIRMATION_transitions_to_CHECKED_IN()
    {
        var now = new DateTime(2026, 10, 15, 7, 55, 0, DateTimeKind.Utc);
        var clock = new FakeClock();
        clock.Set(now);

        var repo = new InMemoryFieldCheckInRepository();
        var assignment = new JobAssignment
        {
            AssignmentId = 103,
            OrderId = 3,
            CustomerId = 12,
            WorkerId = 22,
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now.AddHours(-1)
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        repo.AddAssignment(assignment);

        var geoService = new FakeGeoService();
        var target = new GeoPoint(10.762622, 106.660172);
        var device = new GeoPoint(10.765500, 106.662000);

        var service = new FieldCheckInService(repo, geoService, clock, MicrosoftOptions.Create(new BusinessRules()));

        // Step 1: Initial check-in with GPS deviation
        var initialResult = await service.CheckInAsync(103, 22, device, target);
        Assert.True(initialResult.RequiresFallback);

        // Step 2: Resolve with PLATE_PHOTO
        var fallbackResult = await service.ResolveFallbackCheckInAsync(
            assignmentId: 103,
            fallbackMethod: "PLATE_PHOTO",
            photoUrl: "https://storage.local/plates/plate-103.jpg"
        );

        Assert.True(fallbackResult.Success);
        Assert.Equal("PLATE_PHOTO", fallbackResult.FallbackMethod);
        Assert.Equal(JobAssignmentStatus.CheckedIn, fallbackResult.Status);

        var updatedAssignment = await repo.GetAssignmentAsync(103);
        Assert.Equal(JobAssignmentStatus.CheckedIn, updatedAssignment!.AssignmentStatus);

        var log = await repo.GetCheckInLogAsync(103);
        Assert.Equal("PLATE_PHOTO", log!.FallbackMethod);
        Assert.Equal("https://storage.local/plates/plate-103.jpg", log.FallbackPhotoUrl);
    }
}
