using CommonService.Application.Features.Customers;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure;
using CommonService.Infrastructure.Modules.Customers;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CommonService.Tests.Persistence;

public class RepositoryAndUnitOfWorkTests
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

    [Fact]
    public void UnitOfWork_And_CustomerRepository_Resolve_From_DI_Through_Module()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        services.AddLogging();
        services.AddInfrastructure(config);

        using var provider = services.BuildServiceProvider();

        var uow = provider.GetService<IUnitOfWork>();
        Assert.NotNull(uow);
        Assert.IsType<UnitOfWork>(uow);

        var repo = provider.GetService<ICustomerRepository>();
        Assert.NotNull(repo);
        Assert.IsType<CustomerRepository>(repo);
    }

    [Fact]
    public async Task Repository_And_UnitOfWork_Commit_Atomically_To_SqlServer()
    {
        if (!IsSqlServerAvailable()) return;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        var phone = "097" + Random.Shared.Next(1000000, 9999999);
        int createdCustomerId;

        using (var context = new AppDbContext(options))
        {
            var uow = new UnitOfWork(context);
            var repo = new CustomerRepository(context);

            var customer = new Customer
            {
                PhoneNumber = phone,
                FullName = "Atomic UoW Test",
                AccountStatus = CustomerAccountStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await repo.AddAsync(customer);

            // Before SaveChangesAsync, customer is not yet in the DB
            using (var verifyContext = new AppDbContext(options))
            {
                var beforeSave = await verifyContext.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.PhoneNumber == phone);
                Assert.Null(beforeSave);
            }

            // Commit atomically via UnitOfWork
            var affected = await uow.SaveChangesAsync();
            Assert.True(affected > 0);
            createdCustomerId = customer.CustomerId;
            Assert.True(createdCustomerId > 0);
        }

        // After SaveChangesAsync, customer is committed and retrievable via repository
        using (var verifyContext = new AppDbContext(options))
        {
            var repo = new CustomerRepository(verifyContext);
            var saved = await repo.GetByPhoneNumberAsync(phone);
            Assert.NotNull(saved);
            Assert.Equal("Atomic UoW Test", saved.FullName);
            Assert.Equal(CustomerAccountStatus.Active, saved.AccountStatus);
        }
    }

    [Fact]
    public async Task UnitOfWork_ExecuteInTransactionAsync_Rolls_Back_On_Exception()
    {
        if (!IsSqlServerAvailable()) return;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        var phone = "098" + Random.Shared.Next(1000000, 9999999);

        using (var context = new AppDbContext(options))
        {
            var uow = new UnitOfWork(context);
            var repo = new CustomerRepository(context);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await uow.ExecuteInTransactionAsync(async () =>
                {
                    var customer = new Customer
                    {
                        PhoneNumber = phone,
                        FullName = "Rollback UoW Test",
                        AccountStatus = CustomerAccountStatus.Active,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    await repo.AddAsync(customer);
                    await uow.SaveChangesAsync();

                    throw new InvalidOperationException("Simulated error inside transaction");
#pragma warning disable CS0162
                    return customer.CustomerId;
#pragma warning restore CS0162
                });
            });
        }

        // Verify the customer was rolled back and never committed to SQL Server
        using (var verifyContext = new AppDbContext(options))
        {
            var repo = new CustomerRepository(verifyContext);
            var rolledBack = await repo.GetByPhoneNumberAsync(phone);
            Assert.Null(rolledBack);
        }
    }

    [Fact]
    public async Task UnitOfWork_Explicit_Begin_And_Rollback_Discards_Changes()
    {
        if (!IsSqlServerAvailable()) return;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        var phone = "099" + Random.Shared.Next(1000000, 9999999);

        using (var context = new AppDbContext(options))
        {
            var uow = new UnitOfWork(context);
            var repo = new CustomerRepository(context);

            await uow.BeginTransactionAsync();

            var customer = new Customer
            {
                PhoneNumber = phone,
                FullName = "Explicit Rollback Test",
                AccountStatus = CustomerAccountStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await repo.AddAsync(customer);
            await uow.SaveChangesAsync();

            await uow.RollbackTransactionAsync();
        }

        // Verify the customer was rolled back
        using (var verifyContext = new AppDbContext(options))
        {
            var repo = new CustomerRepository(verifyContext);
            var rolledBack = await repo.GetByPhoneNumberAsync(phone);
            Assert.Null(rolledBack);
        }
    }
}
