using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Admin;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;

namespace CommonService.Tests.Admin;

public class EfAuditLogTests
{
    private const string ConnectionString =
        "Server=localhost;Database=ChoThueGiupViec;Integrated Security=True;TrustServerCertificate=True;";

    private static readonly DateTime Pinned = new(2026, 10, 6, 8, 30, 0, DateTimeKind.Utc);

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

    /// <summary>A context that is never connected: enough to look at what the change tracker holds.</summary>
    private static AppDbContext UnconnectedContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=unused;Database=unused").Options);

    private static (EfAuditLog log, AppDbContext db) Create()
    {
        var clock = new FakeClock();
        clock.Set(Pinned);
        var db = UnconnectedContext();
        return (new EfAuditLog(db, clock), db);
    }

    private static AuditEntry AdminEntry(string? reason = "raise Economy price") =>
        new(AuditActorType.Admin, 7, "PRICE_RULE", "3", "unit_price", "160000", "170000", reason);

    // ---- what is written -----------------------------------------------------------------------

    [Fact]
    public async Task WriteAsync_tracks_one_Added_row_with_every_field_and_the_clock_time()
    {
        var (log, db) = Create();

        await log.WriteAsync(AdminEntry());

        var entry = Assert.Single(db.ChangeTracker.Entries<AdminAuditLog>());
        Assert.Equal(EntityState.Added, entry.State);
        var row = entry.Entity;
        Assert.Equal(AuditActorType.Admin, row.ActorType);
        Assert.Equal(7, row.AdminId);
        Assert.Equal("PRICE_RULE", row.EntityType);
        Assert.Equal("3", row.EntityId);
        Assert.Equal("unit_price", row.FieldName);
        Assert.Equal("160000", row.OldValue);
        Assert.Equal("170000", row.NewValue);
        Assert.Equal("raise Economy price", row.Reason);
        Assert.Equal(Pinned, row.ChangedAt);
        Assert.Equal(DateTimeKind.Utc, row.ChangedAt.Kind);
    }

    [Fact]
    public async Task WriteAsync_does_not_save_by_itself()
    {
        // No connection exists: if WriteAsync called SaveChanges it would throw. It must only track the row.
        var (log, db) = Create();
        await log.WriteAsync(AdminEntry());
        Assert.Equal(EntityState.Added, Assert.Single(db.ChangeTracker.Entries<AdminAuditLog>()).State);
    }

    [Fact]
    public async Task System_entry_without_admin_and_without_reason_is_accepted()
    {
        var (log, db) = Create();
        await log.WriteAsync(new AuditEntry(AuditActorType.System, null, "WORKER", "12", "is_super_freelancer", "true", "false", null));
        var row = Assert.Single(db.ChangeTracker.Entries<AdminAuditLog>()).Entity;
        Assert.Equal(AuditActorType.System, row.ActorType);
        Assert.Null(row.AdminId);
    }

    // ---- validation ------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Admin_entry_needs_a_reason(string? reason)
    {
        var (log, db) = Create();
        await Assert.ThrowsAsync<ArgumentException>(() => log.WriteAsync(AdminEntry(reason)));
        Assert.Empty(db.ChangeTracker.Entries<AdminAuditLog>());
    }

    [Fact]
    public async Task Admin_entry_needs_the_admin_id()
    {
        var (log, _) = Create();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            log.WriteAsync(new AuditEntry(AuditActorType.Admin, null, "PRICE_RULE", "3", "unit_price", "1", "2", "why")));
    }

    [Fact]
    public async Task System_entry_must_not_carry_an_admin_id()
    {
        var (log, _) = Create();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            log.WriteAsync(new AuditEntry(AuditActorType.System, 7, "WORKER", "12", "is_super_freelancer", "true", "false", "x")));
    }

    [Theory]
    [InlineData("entityType", 31)]
    [InlineData("entityId", 41)]
    [InlineData("fieldName", 51)]
    [InlineData("oldValue", 501)]
    [InlineData("newValue", 501)]
    [InlineData("reason", 256)]
    public async Task Over_long_values_are_rejected_not_truncated(string field, int length)
    {
        var (log, db) = Create();
        var text = new string('x', length);
        var entry = field switch
        {
            "entityType" => AdminEntry() with { EntityType = text },
            "entityId" => AdminEntry() with { EntityId = text },
            "fieldName" => AdminEntry() with { FieldName = text },
            "oldValue" => AdminEntry() with { OldValue = text },
            "newValue" => AdminEntry() with { NewValue = text },
            _ => AdminEntry(text),
        };
        await Assert.ThrowsAsync<ArgumentException>(() => log.WriteAsync(entry));
        Assert.Empty(db.ChangeTracker.Entries<AdminAuditLog>());
    }

    [Fact]
    public async Task Values_exactly_at_the_column_size_are_accepted()
    {
        var (log, db) = Create();
        var entry = new AuditEntry(AuditActorType.Admin, 1, new string('a', 30), new string('b', 40), new string('c', 50),
            new string('d', 500), new string('e', 500), new string('f', 255));
        await log.WriteAsync(entry);
        Assert.Single(db.ChangeTracker.Entries<AdminAuditLog>());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Required_text_cannot_be_blank(string blank)
    {
        var (log, _) = Create();
        await Assert.ThrowsAsync<ArgumentException>(() => log.WriteAsync(AdminEntry() with { EntityType = blank }));
        await Assert.ThrowsAsync<ArgumentException>(() => log.WriteAsync(AdminEntry() with { EntityId = blank }));
        await Assert.ThrowsAsync<ArgumentException>(() => log.WriteAsync(AdminEntry() with { FieldName = blank }));
    }

    // ---- append-only API -------------------------------------------------------------------------

    [Fact]
    public void The_port_and_its_implementation_expose_no_update_or_delete()
    {
        var forbidden = new[] { "Update", "Delete", "Remove", "Edit", "Clear" };
        foreach (var type in new[] { typeof(IAuditLog), typeof(EfAuditLog) })
        {
            var methods = type.GetMethods().Select(m => m.Name).Where(n => forbidden.Any(n.Contains));
            Assert.Empty(methods);
        }
    }

    // ---- registration ----------------------------------------------------------------------------

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void AdminModule_registers_the_real_implementation_over_the_Fake()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        services.AddInfrastructure(new ConfigurationBuilder().Build()); // loads every IModule, like Program.cs

        // Same order as Program.cs after the modules: Fakes are added with TryAdd and must lose to the module.
        services.AddFakePorts();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.IsType<EfAuditLog>(scope.ServiceProvider.GetRequiredService<IAuditLog>());
    }

    private sealed class SingletonConsumer(IAuditLog audit)
    {
        public IAuditLog Audit { get; } = audit;
    }

    [Fact]
    public void A_singleton_cannot_take_IAuditLog_because_it_shares_the_scoped_DbContext()
    {
        // Documents the rule of Backend/ARCHITECTURE.md 7.6: FakeAuditLog was a singleton, the real one is scoped
        // (it uses the request's AppDbContext), so a consumer of the port must be scoped or transient.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        services.AddInfrastructure(new ConfigurationBuilder().Build());
        services.AddFakePorts();
        services.AddSingleton<SingletonConsumer>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var error = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<SingletonConsumer>());
        // The chain EfAuditLog -> AppDbContext -> DbContextOptions is scoped, so the singleton is refused.
        Assert.Contains("Cannot consume scoped service", error.Message);
        Assert.Contains(nameof(SingletonConsumer), error.Message);
    }

    // ---- same transaction as the change (SQL Server, skipped when not available) -----------------

    private static DbContextOptions<AppDbContext> SqlOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;

    [Fact]
    public async Task Committed_unit_of_work_keeps_the_audit_row_and_a_rolled_back_one_leaves_none()
    {
        if (!IsSqlServerAvailable()) return;

        var marker = "audit-test-" + Guid.NewGuid().ToString("N")[..12];
        var clock = new FakeClock();

        // 1) rolled back: the row is written inside a transaction that never commits.
        await using (var db = new AppDbContext(SqlOptions()))
        {
            IUnitOfWork uow = new UnitOfWork(db);
            var log = new EfAuditLog(db, clock);
            await uow.BeginTransactionAsync();
            await log.WriteAsync(new AuditEntry(AuditActorType.System, null, "TEST", marker + "-rb", "f", null, "v", null));
            await uow.SaveChangesAsync();
            await uow.RollbackTransactionAsync();
        }

        // 2) committed
        await using (var db = new AppDbContext(SqlOptions()))
        {
            IUnitOfWork uow = new UnitOfWork(db);
            var log = new EfAuditLog(db, clock);
            await uow.BeginTransactionAsync();
            await log.WriteAsync(new AuditEntry(AuditActorType.System, null, "TEST", marker + "-ok", "f", null, "v", null));
            await uow.SaveChangesAsync();
            await uow.CommitTransactionAsync();
        }

        await using var verify = new AppDbContext(SqlOptions());
        Assert.False(await verify.AdminAuditLogs.AnyAsync(x => x.EntityId == marker + "-rb"));
        Assert.True(await verify.AdminAuditLogs.AnyAsync(x => x.EntityId == marker + "-ok"));

        // cleanup of the test row only (test data, not part of the product API)
        await verify.AdminAuditLogs.Where(x => x.EntityId == marker + "-ok").ExecuteDeleteAsync();
    }
}
