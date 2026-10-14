using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Modules.Agencies;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CommonService.Tests.Agencies;

/// <summary>
/// BE-M5-01: <see cref="EfAgencyRepository.AddAsync"/> turns a concurrent duplicate tax code into a 409 and lets every
/// other database failure through. The save is intercepted, so no SQL Server connection is opened.
/// </summary>
public class EfAgencyRepositoryTests
{
    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=unused;Integrated Security=True;TrustServerCertificate=True;")
            .Options;

    private sealed class FailingSaveContext(DbContextOptions<AppDbContext> options, Exception failure) : AppDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<int>(failure);
    }

    private static PartnerAgency NewAgency() => new()
    {
        TaxCode = "0312345678",
        LegalName = "Cong ty ABC",
        LegalRepresentative = "Nguyen Van A",
        ContactPhone = "0901234567",
        ContactEmail = "partner@example.com",
        AgencyStatus = AgencyStatuses.Pending
    };

    [Theory]
    [InlineData(SqlUniqueViolation.DuplicateKeyInUniqueIndex)]
    [InlineData(SqlUniqueViolation.UniqueConstraintViolation)]
    public async Task Unique_violation_on_save_becomes_a_409_and_the_entity_is_detached(int sqlNumber)
    {
        var failure = new DbUpdateException("save failed", SqlUniqueViolationTests.CreateSqlException(sqlNumber));
        await using var context = new FailingSaveContext(Options(), failure);
        var repository = new EfAgencyRepository(context);
        var agency = NewAgency();

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => repository.AddAsync(agency));

        Assert.Equal(AgencyErrorCodes.TaxCodeExists, ex.Code);
        Assert.Same(failure, ex.InnerException);
        Assert.Equal(EntityState.Detached, context.Entry(agency).State);
    }

    [Fact]
    public async Task Other_database_failures_are_not_translated()
    {
        var failure = new DbUpdateException("fk", SqlUniqueViolationTests.CreateSqlException(547));
        await using var context = new FailingSaveContext(Options(), failure);
        var repository = new EfAgencyRepository(context);

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => repository.AddAsync(NewAgency()));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task A_successful_save_adds_the_agency_exactly_once()
    {
        await using var context = new CountingSaveContext(Options());
        var repository = new EfAgencyRepository(context);
        var agency = NewAgency();

        await repository.AddAsync(agency);

        Assert.Equal(1, context.Saves);
        Assert.Single(context.ChangeTracker.Entries<PartnerAgency>());
        Assert.Same(agency, context.ChangeTracker.Entries<PartnerAgency>().Single().Entity);
    }

    private sealed class CountingSaveContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public int Saves { get; private set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }
}
