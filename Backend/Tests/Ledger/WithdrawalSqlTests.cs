using CommonService.Application.Exceptions;
using CommonService.Application.Features.Ledger;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Admin;
using CommonService.Infrastructure.Modules.Ledger;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Ledger;

/// <summary>
/// The SQL side of LEDGER-07: the filtered unique index (one PENDING request per worker), the conditional cancel and the worker lock.
/// Runs only against the scratch database <c>LedgerTicket725Test</c> (set <c>ConnectionStrings__DefaultConnection</c>); rolled back.
/// </summary>
public class WithdrawalSqlTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc);

    private static WithdrawalRequest Request(int workerId, decimal amount, DateTime at) =>
        new() { WorkerId = workerId, Amount = amount, RequestStatus = WithdrawalStatus.Pending, BankAccountNo = "012", BankName = "ACB", RequestedAt = at };

    [Fact]
    public async Task Sql_unique_index_allows_one_pending_request_and_cancel_is_conditional()
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrEmpty(connection) || new SqlConnectionStringBuilder(connection).InitialCatalog != "LedgerTicket725Test") return;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE WITHDRAWAL_REQUEST NOCHECK CONSTRAINT ALL"); // made-up worker ids, rolled back below
        var repository = new EfWithdrawalRepository(db);
        const int worker = 987654;

        var locked = await repository.ExecuteLockedAsync(worker, async () =>
        {
            var first = Request(worker, 100_000m, Now);
            Assert.True(await repository.TryAddAsync(first));
            Assert.True(first.WithdrawalId > 0);
            Assert.False(await repository.TryAddAsync(Request(worker, 50_000m, Now.AddMinutes(1)))); // the filtered unique index
            return first.WithdrawalId;
        });

        var pending = await repository.GetPendingAsync(worker);
        Assert.Equal((locked, 100_000m, WithdrawalStatus.Pending, "012", "ACB"),
            (pending!.WithdrawalId, pending.Amount, pending.RequestStatus, pending.BankAccountNo, pending.BankName));
        Assert.Null(await repository.GetAsync(worker + 1, locked)); // somebody else's request is not visible
        Assert.False(await repository.TryCancelAsync(worker + 1, locked));
        Assert.True(await repository.TryAddAsync(Request(worker + 1, 10_000m, Now))); // another worker has its own pending request

        Assert.True(await repository.TryCancelAsync(worker, locked));
        Assert.False(await repository.TryCancelAsync(worker, locked)); // no longer pending
        Assert.Equal(WithdrawalStatus.Cancelled, (await repository.GetAsync(worker, locked))!.RequestStatus);
        Assert.Null(await repository.GetPendingAsync(worker));

        Assert.True(await repository.TryAddAsync(Request(worker, 70_000m, Now.AddHours(1)))); // a new request after the cancel
        var (items, total) = await repository.ListAsync(worker, 1, 10);
        Assert.Equal(2, total);
        Assert.Equal([70_000m, 100_000m], items.Select(i => i.Amount).ToArray());
        Assert.Single((await repository.ListAsync(worker, 2, 1)).Items);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Sql_worker_lock_is_taken_inside_its_own_transaction_and_released_at_the_end()
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrEmpty(connection) || new SqlConnectionStringBuilder(connection).InitialCatalog != "LedgerTicket725Test") return;
        await using var first = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
        await using var second = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);

        // Without an outer transaction the repository opens one, takes the lock and commits; a second context can take it afterwards.
        Assert.Equal(1, await new EfWithdrawalRepository(first).ExecuteLockedAsync(987655, () => Task.FromResult(1)));
        Assert.Equal(2, await new EfWithdrawalRepository(second).ExecuteLockedAsync(987655, () => Task.FromResult(2)));
        Assert.Null(first.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Sql_approval_writes_one_ledger_entry_and_pays_the_request_in_one_transaction_and_rejection_writes_none()
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrEmpty(connection) || new SqlConnectionStringBuilder(connection).InitialCatalog != "LedgerTicket725Test") return;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE WITHDRAWAL_REQUEST NOCHECK CONSTRAINT ALL"); // made-up worker and admin ids, rolled back below
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE ADMIN_AUDIT_LOG NOCHECK CONSTRAINT ALL");
        const int worker = 987660;
        const int admin = 5;
        var clock = new FakeClock();
        clock.Set(Now);
        var ledger = new EfLedgerRepository(db);
        var writer = new LedgerWriter(ledger, clock);
        var repository = new EfWithdrawalRepository(db);
        var notifications = new FakeNotificationService();
        var service = new AdminWithdrawalService(
            repository, new LedgerBalanceService(new EfLedgerQuery(db), new FakeOpenDisputeQuery(), clock), writer, new EfAuditLog(db, clock), notifications, clock,
            NullLogger<AdminWithdrawalService>.Instance);

        await writer.AppendAsync([new NewLedgerEntry(LedgerRules.Clearing, new LedgerParty(LedgerPartyType.Worker, worker), 288_000m, LedgerEntryType.JobEarning,
            LedgerRefType.Assignment, 1, null, Now.AddHours(-1), "sql-earn:" + Guid.NewGuid().ToString("N"))]);
        var first = Request(worker, 100_000m, Now);
        Assert.True(await repository.TryAddAsync(first));

        var approved = await service.ApproveAsync(admin, first.WithdrawalId);
        Assert.Equal("PAID", approved.Status);
        var entry = await db.LedgerEntries.AsNoTracking().SingleAsync(e => e.EntryType == LedgerEntryType.Withdrawal && e.FromPartyId == worker);
        Assert.Equal((100_000m, $"WITHDRAWAL:{first.WithdrawalId}", admin), (entry.Amount, entry.IdempotencyKey, entry.CreatedByAdminId!.Value));
        var paid = await db.WithdrawalRequests.AsNoTracking().SingleAsync(w => w.WithdrawalId == first.WithdrawalId);
        Assert.Equal((WithdrawalStatus.Paid, entry.EntryId, admin), (paid.RequestStatus, paid.EntryId, paid.ReviewedBy));
        Assert.NotNull(paid.ReviewedAt);
        Assert.Equal("PAID", (await db.AdminAuditLogs.AsNoTracking().SingleAsync(a => a.EntityType == "WITHDRAWAL_REQUEST" && a.EntityId == first.WithdrawalId.ToString())).NewValue);
        Assert.Equal("WITHDRAWAL_NOT_PENDING", (await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.ApproveAsync(admin, first.WithdrawalId))).Code);
        Assert.Equal(1, await db.LedgerEntries.CountAsync(e => e.EntryType == LedgerEntryType.Withdrawal));

        // A second request is rejected: no entry. A third asks one dong more than what is left (288,000 - 100,000 = 188,000): insufficient.
        var second = Request(worker, 50_000m, Now.AddMinutes(1));
        Assert.True(await repository.TryAddAsync(second));
        var rejected = await service.RejectAsync(admin, second.WithdrawalId, "Không hợp lệ");
        Assert.Equal(("REJECTED", "Không hợp lệ"), (rejected.Status, rejected.RejectReason));
        Assert.Equal(1, await db.LedgerEntries.CountAsync(e => e.EntryType == LedgerEntryType.Withdrawal));

        var third = Request(worker, 188_001m, Now.AddMinutes(2));
        Assert.True(await repository.TryAddAsync(third));
        Assert.Equal("WITHDRAWAL_INSUFFICIENT", (await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.ApproveAsync(admin, third.WithdrawalId))).Code);
        Assert.Equal(WithdrawalStatus.Pending, (await repository.GetByIdAsync(third.WithdrawalId))!.RequestStatus);

        var pending = await service.ListAsync("PENDING", null, null);
        Assert.Equal(third.WithdrawalId, Assert.Single(pending.Items, i => i.WorkerId == worker).WithdrawalId);
        Assert.Equal(first.WithdrawalId, Assert.Single((await service.ListAsync("paid", null, null)).Items, i => i.WorkerId == worker).WithdrawalId);
        Assert.Equal(3, (await service.ListAsync(null, null, null)).Items.Count(i => i.WorkerId == worker));
        Assert.Equal(2, notifications.Sent.Count);
        await transaction.RollbackAsync();
    }
}
