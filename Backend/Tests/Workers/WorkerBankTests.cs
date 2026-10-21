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

/// <summary>ECON-FLOW-FIX: the account the worker's monthly payout is transferred to.</summary>
public class WorkerBankTests
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

    [Fact]
    public void A_valid_pair_is_trimmed_and_the_account_loses_its_spaces() =>
        Assert.Equal(("Vietcombank", "0011001234567"), WorkerBankValidator.Validate("  Vietcombank ", "0011 0012 34567"));

    [Fact]
    public void Both_fields_are_required()
    {
        var errors = Assert.Throws<ValidationException>(() => WorkerBankValidator.Validate(null, "")).Errors;

        Assert.Contains("bankName", errors.Keys);
        Assert.Contains("bankAccountNo", errors.Keys);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("12345678901234567890123456789012")]
    [InlineData("00110012A4")]
    public void An_account_number_must_be_6_to_30_digits(string account) =>
        Assert.Contains("bankAccountNo", Assert.Throws<ValidationException>(() => WorkerBankValidator.Validate("VCB", account)).Errors.Keys);

    [Fact]
    public void A_bank_name_over_the_column_size_is_rejected() =>
        Assert.Contains("bankName", Assert.Throws<ValidationException>(() => WorkerBankValidator.Validate(new string('B', 101), "0011001234")).Errors.Keys);

    [Fact]
    public async Task A_missing_body_and_an_anonymous_caller_are_refused()
    {
        var update = new WorkerProfileService(null!, new User(7), new FakeClock(), null!);
        await Assert.ThrowsAsync<ValidationException>(() => update.UpdateBankAsync(null, default));

        var anonymous = new WorkerProfileService(null!, new User(null), new FakeClock(), null!);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => anonymous.UpdateBankAsync(new UpdateWorkerBankRequest("VCB", "0011001234"), default));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => new WorkerProfileService(null!, new User(null), new FakeClock(), null!).GetBankAsync(default));
    }

    [Fact]
    public async Task The_worker_sets_and_reads_back_the_account()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = new StampingDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);
        using var transaction = await db.Database.BeginTransactionAsync();
        var worker = Worker.CreateFreelancer("09" + Random.Shared.Next(10000000, 99999999), "012" + Random.Shared.Next(100000000, 999999999), "Banked");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();
        IWorkerRepository repository = new EfWorkerRepository(db);
        var user = new User(worker.WorkerId);

        Assert.False((await new WorkerProfileService(repository, user, new FakeClock(), null!).GetBankAsync(default)).Data!.IsSet);

        await new WorkerProfileService(repository, user, new FakeClock(), null!)
            .UpdateBankAsync(new UpdateWorkerBankRequest("Vietcombank", "0011 0012 3456"), default);

        var read = (await new WorkerProfileService(repository, user, new FakeClock(), null!).GetBankAsync(default)).Data!;
        Assert.Equal(("Vietcombank", "001100123456", true), (read.BankName, read.BankAccountNo, read.IsSet));

        await transaction.RollbackAsync();
    }
}
