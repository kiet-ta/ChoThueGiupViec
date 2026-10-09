using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Commands;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Modules.Workers;
using CommonService.Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CommonService.Tests.Workers;

public class JobAcceptanceTests
{
    private const string ConnectionString =
        "Server=localhost;Database=ChoThueGiupViec;Integrated Security=True;TrustServerCertificate=True;";

    private static bool IsSqlServerAvailable()
    {
        try
        {
            using var conn = new SqlConnection(ConnectionString + "Connect Timeout=3;");
            conn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task SubmitCompletion_ValidPhotos_TransitionsToAwaitingAcceptance()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var phone = $"099{Random.Shared.Next(1000000, 9999999)}";
        var nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";
        var worker = Worker.CreateFreelancer(phone, nationalId, "Acceptance Worker");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var assignment = new JobAssignment
        {
            OrderId = Random.Shared.Next(10000, 99999),
            CustomerId = 200,
            WorkerId = worker.WorkerId,
            SlotId = 10,
            ServiceTier = ServiceTier.Economy,
            GrossAmount = 260000m,
            CommissionRate = 0.20m,
            PayoutAmount = 0m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
        assignment.TransitionTo(JobAssignmentStatus.InProgress);

        await db.JobAssignments.AddAsync(assignment);
        await db.SaveChangesAsync();

        // Add 3 accepted BEFORE photos (angles 1, 2, 3)
        for (byte i = 1; i <= 3; i++)
        {
            await db.JobPhotos.AddAsync(new JobPhoto
            {
                AssignmentId = assignment.AssignmentId,
                PhotoPhase = "BEFORE",
                AngleNo = i,
                ImageUrl = $"https://img.local/before_{i}.jpg",
                VolScore = 120.0,
                IsAccepted = true,
                CapturedAt = DateTime.UtcNow
            });
        }

        // Add 3 accepted AFTER photos (angles 1, 2, 3)
        for (byte i = 1; i <= 3; i++)
        {
            await db.JobPhotos.AddAsync(new JobPhoto
            {
                AssignmentId = assignment.AssignmentId,
                PhotoPhase = "AFTER",
                AngleNo = i,
                ImageUrl = $"https://img.local/after_{i}.jpg",
                VolScore = 130.0,
                IsAccepted = true,
                CapturedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();

        var repository = new EfWorkerRepository(db);
        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };
        var handler = new SubmitJobCompletionCommandHandler(repository, currentUser);

        // Act
        var response = await handler.Handle(new SubmitJobCompletionCommand(assignment.AssignmentId, "Done clean"), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("AWAITING_ACCEPTANCE", response.Data!.Status);

        var updatedAssignment = await db.JobAssignments.FindAsync(assignment.AssignmentId);
        Assert.Equal(JobAssignmentStatus.AwaitingAcceptance, updatedAssignment!.AssignmentStatus);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task SubmitCompletion_MissingPhotos_ThrowsValidationException()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var phone = $"099{Random.Shared.Next(1000000, 9999999)}";
        var nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";
        var worker = Worker.CreateFreelancer(phone, nationalId, "Acceptance Worker 2");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var assignment = new JobAssignment
        {
            OrderId = Random.Shared.Next(10000, 99999),
            CustomerId = 200,
            WorkerId = worker.WorkerId,
            SlotId = 11,
            ServiceTier = ServiceTier.Economy,
            GrossAmount = 260000m,
            CommissionRate = 0.20m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
        assignment.TransitionTo(JobAssignmentStatus.InProgress);

        await db.JobAssignments.AddAsync(assignment);
        await db.SaveChangesAsync();

        var repository = new EfWorkerRepository(db);
        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };
        var handler = new SubmitJobCompletionCommandHandler(repository, currentUser);

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new SubmitJobCompletionCommand(assignment.AssignmentId), CancellationToken.None));

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task RequestRedo_ValidAwaitingAcceptance_TransitionsToInProgress()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var phone = $"099{Random.Shared.Next(1000000, 9999999)}";
        var nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";
        var worker = Worker.CreateFreelancer(phone, nationalId, "Acceptance Worker 3");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var assignment = new JobAssignment
        {
            OrderId = Random.Shared.Next(10000, 99999),
            CustomerId = 200,
            WorkerId = worker.WorkerId,
            SlotId = 12,
            ServiceTier = ServiceTier.Economy,
            GrossAmount = 260000m,
            CommissionRate = 0.20m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
        assignment.TransitionTo(JobAssignmentStatus.InProgress);
        assignment.TransitionTo(JobAssignmentStatus.AwaitingAcceptance);

        await db.JobAssignments.AddAsync(assignment);
        await db.SaveChangesAsync();

        var repository = new EfWorkerRepository(db);
        var currentUser = new TestCurrentUser { UserId = 200, Role = UserRole.Customer };
        var handler = new RequestJobRedoCommandHandler(repository, currentUser);

        // Act
        var response = await handler.Handle(
            new RequestJobRedoCommand(assignment.AssignmentId, new RequestRedoRequest("Chua sach dust")),
            CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("IN_PROGRESS", response.Data!.Status);
        Assert.Contains("Chua sach dust", response.Data.Notes);

        var updated = await db.JobAssignments.FindAsync(assignment.AssignmentId);
        Assert.Equal(JobAssignmentStatus.InProgress, updated!.AssignmentStatus);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task AcceptJob_ValidFreelancer_CalculatesPayout80Percent_SetsWorkerIdle_EmitsJobCompleted()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var phone = $"099{Random.Shared.Next(1000000, 9999999)}";
        var nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";
        var worker = Worker.CreateFreelancer(phone, nationalId, "Acceptance Worker 4");
        worker.SetKycResult(90.0m, "APPROVED");
        worker.TransitionTo(WorkStatus.Busy);
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var assignment = new JobAssignment
        {
            OrderId = Random.Shared.Next(10000, 99999),
            CustomerId = 200,
            WorkerId = worker.WorkerId,
            AgencyId = null,
            SlotId = 13,
            ServiceTier = ServiceTier.Economy,
            GrossAmount = 260000m,
            CommissionRate = 0.20m,
            PayoutAmount = 0m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
        assignment.TransitionTo(JobAssignmentStatus.InProgress);
        assignment.TransitionTo(JobAssignmentStatus.AwaitingAcceptance);

        await db.JobAssignments.AddAsync(assignment);
        await db.SaveChangesAsync();

        var repository = new EfWorkerRepository(db);
        var currentUser = new TestCurrentUser { UserId = 200, Role = UserRole.Customer };
        var publisher = new TestPublisher();
        var handler = new AcceptJobCompletionCommandHandler(repository, publisher, currentUser);

        // Act
        var response = await handler.Handle(new AcceptJobCompletionCommand(assignment.AssignmentId), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("COMPLETED", response.Data!.Status);
        Assert.Equal(260000m, response.Data.GrossAmount);
        Assert.Equal(0.20m, response.Data.CommissionRate);
        Assert.Equal(208000m, response.Data.PayoutAmount); // 80% of 260k

        var updatedAssignment = await db.JobAssignments.FindAsync(assignment.AssignmentId);
        Assert.Equal(JobAssignmentStatus.Completed, updatedAssignment!.AssignmentStatus);
        Assert.Equal(208000m, updatedAssignment.PayoutAmount);
        Assert.NotNull(updatedAssignment.CompletedAt);

        var updatedWorker = await db.Workers.FindAsync(worker.WorkerId);
        Assert.Equal(WorkStatus.Idle, updatedWorker!.WorkStatus);
        Assert.Equal(1, updatedWorker.CompletedJobs);

        Assert.Single(publisher.PublishedEvents);
        var jobCompletedEvent = Assert.IsType<JobCompleted>(publisher.PublishedEvents[0]);
        Assert.Equal(assignment.AssignmentId, jobCompletedEvent.AssignmentId);
        Assert.Equal(208000m, jobCompletedEvent.PayoutAmount);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task AcceptJob_ValidAgencyWorker_CalculatesPayoutUsingPackageCommission()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var phone = $"099{Random.Shared.Next(1000000, 9999999)}";
        var nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";
        var worker = Worker.CreateAgencyStaff(5, phone, nationalId, "Agency Staff Worker");
        worker.TransitionTo(WorkStatus.Busy);
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var assignment = new JobAssignment
        {
            OrderId = Random.Shared.Next(10000, 99999),
            CustomerId = 200,
            WorkerId = worker.WorkerId,
            AgencyId = 5,
            SlotId = 14,
            ServiceTier = ServiceTier.Premium,
            GrossAmount = 390000m,
            CommissionRate = 0.00m, // PRO agency package (0% commission)
            PayoutAmount = 0m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
        assignment.TransitionTo(JobAssignmentStatus.InProgress);
        assignment.TransitionTo(JobAssignmentStatus.AwaitingAcceptance);

        await db.JobAssignments.AddAsync(assignment);
        await db.SaveChangesAsync();

        var repository = new EfWorkerRepository(db);
        var currentUser = new TestCurrentUser { UserId = 200, Role = UserRole.Customer };
        var publisher = new TestPublisher();
        var handler = new AcceptJobCompletionCommandHandler(repository, publisher, currentUser);

        // Act
        var response = await handler.Handle(new AcceptJobCompletionCommand(assignment.AssignmentId), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(390000m, response.Data!.PayoutAmount); // 100% of 390k (0% commission)

        await transaction.RollbackAsync();
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId { get; set; }
        public UserRole? Role { get; set; } = UserRole.Worker;
        public bool IsAuthenticated => UserId.HasValue;
    }

    private sealed class TestPublisher : IPublisher
    {
        public List<INotification> PublishedEvents { get; } = new();

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (notification is INotification n)
            {
                PublishedEvents.Add(n);
            }
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
        {
            PublishedEvents.Add(notification);
            return Task.CompletedTask;
        }
    }
}
