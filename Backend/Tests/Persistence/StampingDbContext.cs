using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Persistence;

/// <summary>
/// The context of the older SQL Server integration tests. Production handlers set <c>created_at</c> / <c>updated_at</c> themselves, and the
/// context refuses a <see cref="DateTime"/> that is not UTC; those tests build entities through the domain factories and never set them, so
/// this context fills a missing (default) timestamp with the current UTC time and marks any other one as UTC before the save.
/// </summary>
internal sealed class StampingDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
{
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Stamp();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        Stamp();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void Stamp()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            foreach (var property in entry.Properties.Where(p => p.CurrentValue is DateTime))
            {
                var value = (DateTime)property.CurrentValue!;
                if (value == default)
                {
                    property.CurrentValue = DateTime.UtcNow;
                }
                else if (value.Kind != DateTimeKind.Utc)
                {
                    property.CurrentValue = DateTime.SpecifyKind(value, DateTimeKind.Utc);
                }
            }
        }
    }
}
