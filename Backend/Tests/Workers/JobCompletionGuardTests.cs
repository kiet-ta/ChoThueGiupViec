using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using MediatR;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>
/// WORKERS-SEC-02: the ownership guards of submit, accept and redo fail closed. No database needed: the repository is a stub whose
/// assignment is still <c>OFFERED</c>, so a caller that passes the guard is stopped by the status rule (409) and nothing is written.
/// </summary>
public class JobCompletionGuardTests
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

    private sealed class NoPublisher : IPublisher
    {
        public int Published { get; private set; }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Published++;
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
        {
            Published++;
            return Task.CompletedTask;
        }
    }

    /// <summary>One OFFERED assignment; only the members the guards use are implemented, a write is a test failure.</summary>
    private sealed class AssignmentRepository(bool assignmentExists = true) : IWorkerRepository
    {
        public int AssignmentLookups { get; private set; }

        public Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default)
        {
            AssignmentLookups++;
            JobAssignment? found = assignmentExists && assignmentId == AssignmentId
                ? new JobAssignment { AssignmentId = AssignmentId, WorkerId = WorkerId, CustomerId = CustomerId }
                : null;
            return Task.FromResult(found);
        }

        public Task<Worker?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByNationalIdAsync(string nationalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByPhoneOrNationalIdAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<Worker> Items, int TotalCount)> GetEkycQueueAsync(int page, int pageSize, string status = EkycQueueStatus.Pending, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BookingSlot>> GetWorkerSlotsAsync(int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BookingSlot?> GetWorkerSlotByShiftAsync(int workerId, DateOnly slotDate, string shiftCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddSlotAsync(BookingSlot slot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void UpdateSlot(BookingSlot slot) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobPhoto>> GetJobPhotosAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

    // The three actions, with the role that may call each and the id of the party that owns the assignment.
    private static (UserRole Role, int OwnerId) Actor(string action) =>
        action == "submit" ? (UserRole.Worker, WorkerId) : (UserRole.Customer, CustomerId);

    private static Task Act(string action, JobCompletionService service) => action switch
    {
        "submit" => service.SubmitAsync(AssignmentId, "done"),
        "accept" => service.AcceptAsync(AssignmentId, null),
        "redo" => service.RequestRedoAsync(AssignmentId, new RequestRedoRequest("Chua sach")),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    private static JobCompletionService Service(AssignmentRepository repository, ICurrentUser user, NoPublisher? publisher = null) =>
        new(repository, publisher ?? new NoPublisher(), user);

    [Theory]
    [InlineData("submit", null, true)] // the role claim but no numeric id
    [InlineData("accept", null, true)]
    [InlineData("redo", null, true)]
    [InlineData("submit", 0, true)] // id 0
    [InlineData("accept", 0, true)]
    [InlineData("redo", 0, true)]
    [InlineData("submit", WorkerId, false)] // not authenticated
    [InlineData("accept", CustomerId, false)]
    [InlineData("redo", CustomerId, false)]
    public async Task AMissingOrInvalidIdentity_FailsClosed_BeforeTheAssignmentIsRead(string action, int? id, bool authenticated)
    {
        var repository = new AssignmentRepository();
        var publisher = new NoPublisher();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Act(action, Service(repository, new User(id, Actor(action).Role, authenticated), publisher)));

        Assert.Equal(0, repository.AssignmentLookups);
        Assert.Equal(0, publisher.Published);
    }

    [Theory]
    [InlineData("submit", UserRole.Customer)]
    [InlineData("submit", UserRole.Admin)]
    [InlineData("accept", UserRole.Worker)]
    [InlineData("accept", UserRole.Admin)]
    [InlineData("redo", UserRole.Worker)]
    [InlineData("redo", UserRole.Partner)]
    public async Task AnotherRole_IsRefused_EvenWithTheOwnersId(string action, UserRole role)
    {
        var repository = new AssignmentRepository();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Act(action, Service(repository, new User(Actor(action).OwnerId, role))));

        Assert.Equal(0, repository.AssignmentLookups);
    }

    [Theory]
    [InlineData("submit")]
    [InlineData("accept")]
    [InlineData("redo")]
    public async Task ACallerWhoDoesNotOwnTheAssignment_IsRefused(string action)
    {
        var (role, ownerId) = Actor(action);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Act(action, Service(new AssignmentRepository(), new User(ownerId + 1, role))));
    }

    [Theory]
    [InlineData("submit")]
    [InlineData("accept")]
    [InlineData("redo")]
    public async Task TheOwner_PassesTheGuard_AndIsStoppedByTheStatusRule(string action)
    {
        var (role, ownerId) = Actor(action);

        // OFFERED is neither IN_PROGRESS (submit) nor AWAITING_ACCEPTANCE (accept, redo): 409, the same order of checks as before.
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            Act(action, Service(new AssignmentRepository(), new User(ownerId, role))));
    }

    [Theory]
    [InlineData("submit")]
    [InlineData("accept")]
    [InlineData("redo")]
    public async Task AnUnknownAssignment_IsNotFound_ForTheRightCaller(string action)
    {
        var (role, ownerId) = Actor(action);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Act(action, Service(new AssignmentRepository(assignmentExists: false), new User(ownerId, role))));
    }

    [Fact]
    public async Task ARedoWithoutAReason_ByAnotherRole_IsRefusedNotValidated()
    {
        // The identity check comes first: a wrong caller learns nothing from a 400.
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Service(new AssignmentRepository(), new User(WorkerId, UserRole.Worker))
                .RequestRedoAsync(AssignmentId, new RequestRedoRequest("")));
    }
}
