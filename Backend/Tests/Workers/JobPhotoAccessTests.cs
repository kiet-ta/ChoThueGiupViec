using CommonService.Infrastructure.Fakes;
using System.Reflection;
using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.WebAPI.Controllers.Workers;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>
/// WORKERS-SEC-01: <c>GET /api/workers/assignments/{id}/photos</c> is for the assignment's worker, its customer or an Admin
/// (contract workers.md 2.4.2). No database needed: the repository is a stub.
/// </summary>
public class JobPhotoAccessTests
{
    private const long AssignmentId = 55;
    private const int WorkerId = 7;
    private const int CustomerId = 21;

    private sealed class User(int? id, UserRole? role, bool authenticated = true) : ICurrentUser
    {
        public int? UserId { get; } = id;
        public UserRole? Role { get; } = role;
        public bool IsAuthenticated { get; } = authenticated;
    }

    /// <summary>One assignment with two photos; only the members the read uses are implemented.</summary>
    private sealed class AssignmentRepository(bool assignmentExists = true) : IWorkerRepository
    {
        public int AssignmentLookups { get; private set; }
        public int PhotoReads { get; private set; }

        public Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default)
        {
            AssignmentLookups++;
            JobAssignment? found = assignmentExists && assignmentId == AssignmentId
                ? new JobAssignment { AssignmentId = AssignmentId, WorkerId = WorkerId, CustomerId = CustomerId }
                : null;
            return Task.FromResult(found);
        }

        public Task<IReadOnlyList<JobPhoto>> GetJobPhotosAsync(long assignmentId, CancellationToken cancellationToken = default)
        {
            PhotoReads++;
            IReadOnlyList<JobPhoto> photos =
            [
                new JobPhoto { PhotoId = 1, AssignmentId = assignmentId, PhotoPhase = "BEFORE", AngleNo = 1, ImageUrl = "https://storage.local/jobs/a1.jpg", IsAccepted = true },
                new JobPhoto { PhotoId = 2, AssignmentId = assignmentId, PhotoPhase = "BEFORE", AngleNo = 2, ImageUrl = "https://storage.local/jobs/a2.jpg", IsAccepted = true },
            ];
            return Task.FromResult(photos);
        }

        public Task<Worker?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByNationalIdAsync(string nationalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByPhoneOrNationalIdAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<Worker> Items, int TotalCount)> GetEkycQueueAsync(int page, int pageSize, string status = EkycQueueStatus.Pending, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BookingSlot>> GetWorkerSlotsAsync(int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BookingSlot?> GetWorkerSlotByShiftAsync(int workerId, DateOnly slotDate, string shiftCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddSlotAsync(BookingSlot slot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void UpdateSlot(BookingSlot slot) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobPhoto>> GetAcceptedJobPhotosByPhaseAsync(long assignmentId, string photoPhase, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddJobPhotoAsync(JobPhoto photo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCompleteAssignmentAsync(long assignmentId, int workerId, decimal commissionRate, decimal payoutAmount, DateTime completedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Worker>> ListAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker> AddAsync(Worker entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Update(Worker entity) => throw new NotSupportedException();
        public void Delete(Worker entity) => throw new NotSupportedException();
    }

    private static JobPhotoService Service(AssignmentRepository repository, ICurrentUser user) =>
        new(repository, null!, user, Microsoft.Extensions.Options.Options.Create(new BusinessRules()), new FakeClock(), new FakeFileStorage(), new JobPhotoUploadPolicy(true));

    [Fact]
    public async Task GetPhotosAsync_TheAssignedWorker_GetsThePhotos()
    {
        var result = await Service(new AssignmentRepository(), new User(WorkerId, UserRole.Worker)).GetPhotosAsync(AssignmentId);

        Assert.Equal(2, result.Data!.Count);
    }

    [Fact]
    public async Task GetPhotosAsync_AnotherWorker_IsRefused()
    {
        var repository = new AssignmentRepository();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Service(repository, new User(WorkerId + 1, UserRole.Worker)).GetPhotosAsync(AssignmentId));
        Assert.Equal(0, repository.PhotoReads);
    }

    [Fact]
    public async Task GetPhotosAsync_TheOwningCustomer_GetsThePhotos()
    {
        var result = await Service(new AssignmentRepository(), new User(CustomerId, UserRole.Customer)).GetPhotosAsync(AssignmentId);

        Assert.Equal(2, result.Data!.Count);
    }

    [Fact]
    public async Task GetPhotosAsync_AnotherCustomer_IsRefused()
    {
        var repository = new AssignmentRepository();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Service(repository, new User(CustomerId + 1, UserRole.Customer)).GetPhotosAsync(AssignmentId));
        Assert.Equal(0, repository.PhotoReads);
    }

    [Fact]
    public async Task GetPhotosAsync_AnAdmin_GetsThePhotosOfAnyAssignment()
    {
        // The admin id matches neither the worker nor the customer id.
        var result = await Service(new AssignmentRepository(), new User(999, UserRole.Admin)).GetPhotosAsync(AssignmentId);

        Assert.Equal(2, result.Data!.Count);
    }

    [Fact]
    public async Task GetPhotosAsync_AnUnknownAssignment_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(new AssignmentRepository(assignmentExists: false), new User(WorkerId, UserRole.Worker)).GetPhotosAsync(AssignmentId));
    }

    [Fact]
    public async Task GetPhotosAsync_APartner_IsRefusedBeforeTheAssignmentIsLookedUp()
    {
        var repository = new AssignmentRepository();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Service(repository, new User(WorkerId, UserRole.Partner)).GetPhotosAsync(AssignmentId));
        Assert.Equal(0, repository.AssignmentLookups);
    }

    [Theory]
    [InlineData(null, UserRole.Worker, true)] // role claim but no numeric id
    [InlineData(0, UserRole.Customer, true)] // id 0
    [InlineData(WorkerId, UserRole.Worker, false)] // not authenticated
    [InlineData(WorkerId, null, true)] // no role
    public async Task GetPhotosAsync_AMissingOrInvalidIdentity_FailsClosed(int? id, UserRole? role, bool authenticated)
    {
        var repository = new AssignmentRepository();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Service(repository, new User(id, role, authenticated)).GetPhotosAsync(AssignmentId));
        Assert.Equal(0, repository.AssignmentLookups);
    }

    [Fact]
    public void GetPhotos_Endpoint_IsLimitedToWorkerCustomerAndAdmin()
    {
        var action = typeof(WorkersController).GetMethod(nameof(WorkersController.GetPhotos))!;
        var roles = action.GetCustomAttributes<AuthorizeAttribute>()
            .SelectMany(a => (a.Roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Order()
            .ToArray();

        Assert.Equal(["Admin", "Customer", "Worker"], roles);
    }
}
