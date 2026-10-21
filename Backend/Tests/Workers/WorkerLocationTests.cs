using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Workers;
using CommonService.Infrastructure.Persistence;
using CommonService.Tests.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>ECON-FLOW-FIX: the worker tells the system where they are (the dispatch search needs it).</summary>
public class WorkerLocationTests
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

    private sealed class User(int? id) : ICurrentUser
    {
        public int? UserId { get; } = id;
        public UserRole? Role { get; } = UserRole.Worker;
        public bool IsAuthenticated => id.HasValue;
    }

    // The validation runs before the repository is read, so the validation tests need no database.
    private static WorkerProfileService Handler(IWorkerRepository? repository, int? userId = 7) =>
        new(repository!, new User(userId), new FakeClock(), null!);

    private static Task<CommonService.Application.Common.Models.ApiResponse<WorkerLocationResponse>> Send(
        WorkerProfileService handler, double? lat, double? lng) =>
        handler.UpdateLocationAsync(new UpdateWorkerLocationRequest(lat, lng), default);

    [Theory]
    [InlineData(91.0, 106.7, "latitude")]
    [InlineData(-90.5, 106.7, "latitude")]
    [InlineData(10.77, 181.0, "longitude")]
    [InlineData(10.77, -180.1, "longitude")]
    [InlineData(null, 106.7, "latitude")]
    [InlineData(10.77, null, "longitude")]
    public async Task A_position_out_of_range_or_missing_is_a_validation_error(double? lat, double? lng, string field)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Send(Handler(null), lat, lng));

        Assert.Contains(field, ex.Errors.Keys);
    }

    [Fact]
    public async Task Both_wrong_fields_are_reported_together()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Send(Handler(null), 200, 400));

        Assert.Equal(2, ex.Errors.Count);
    }

    [Fact]
    public async Task A_missing_body_and_an_anonymous_caller_are_refused()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Handler(null).UpdateLocationAsync(null, default));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Send(Handler(null, userId: null), 10, 106));
    }

    [Fact]
    public async Task The_position_is_stored_on_the_worker_with_six_decimals()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = new StampingDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);
        using var transaction = await db.Database.BeginTransactionAsync();
        var worker = Worker.CreateFreelancer("09" + Random.Shared.Next(10000000, 99999999), "012" + Random.Shared.Next(100000000, 999999999), "Located");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var response = await Send(Handler(new EfWorkerRepository(db), worker.WorkerId), 10.7769123456, 106.7009);

        Assert.Equal(10.776912, response.Data!.Latitude);
        var stored = await db.Workers.AsNoTracking().SingleAsync(w => w.WorkerId == worker.WorkerId);
        Assert.Equal(10.776912m, stored.CurrentLat);
        Assert.Equal(106.7009m, stored.CurrentLng);

        await transaction.RollbackAsync();
    }
}
