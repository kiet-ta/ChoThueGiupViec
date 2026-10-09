using CommonService.Application.Features.Booking;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Booking;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CommonService.Tests.Booking;

/// <summary>DB-backed: runs against the local SQL Server like the other persistence tests; every change is rolled back.</summary>
public sealed class PriceRuleRepositoryTests
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

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    /// <summary>The (Economy, UP_TO_30) row, created inside the open transaction when the DB was never seeded.</summary>
    private static async Task<PriceRule> EnsureRowAsync(AppDbContext context)
    {
        var row = await context.PriceRules.FirstOrDefaultAsync(
            r => r.ServiceTier == ServiceTier.Economy && r.AreaBracket == AreaBrackets.UpTo30);
        if (row is null)
        {
            row = new PriceRule
            {
                ServiceTier = ServiceTier.Economy,
                AreaBracket = AreaBrackets.UpTo30,
                UnitPrice = 160000m,
                IsActive = true,
                UpdatedAt = DateTime.UtcNow,
            };
            context.PriceRules.Add(row);
        }

        row.IsActive = true;
        await context.SaveChangesAsync();
        return row;
    }

    [Fact]
    public async Task GetActiveAsync_ReturnsTheRowOfThePair()
    {
        if (!IsSqlServerAvailable())
        {
            return;
        }

        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var row = await EnsureRowAsync(context);

        var found = await new PriceRuleRepository(context).GetActiveAsync(ServiceTier.Economy, AreaBrackets.UpTo30);

        Assert.NotNull(found);
        Assert.Equal(row.RuleId, found.RuleId);
        Assert.Equal(row.UnitPrice, found.UnitPrice);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task GetActiveAsync_IgnoresInactiveRow()
    {
        if (!IsSqlServerAvailable())
        {
            return;
        }

        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var row = await EnsureRowAsync(context);
        row.IsActive = false;
        await context.SaveChangesAsync();

        var found = await new PriceRuleRepository(context).GetActiveAsync(ServiceTier.Economy, AreaBrackets.UpTo30);

        Assert.Null(found);
        await transaction.RollbackAsync();
    }
}
