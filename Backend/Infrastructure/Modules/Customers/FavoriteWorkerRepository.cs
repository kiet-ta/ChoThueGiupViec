using CommonService.Application.Features.Customers;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Customers;

/// <summary>
/// FAVORITE_WORKER repository of the Customers module (BE-M1-06). Add and remove are idempotent and persist
/// on their own (see <see cref="IFavoriteWorkerRepository"/>).
/// </summary>
public class FavoriteWorkerRepository : IFavoriteWorkerRepository
{
    private const int SqlPrimaryKeyViolation = 2627;
    private const int SqlUniqueIndexViolation = 2601;

    private readonly AppDbContext _context;

    public FavoriteWorkerRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<FavoriteWorker>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await _context.FavoriteWorkers
            .AsNoTracking()
            .Where(f => f.CustomerId == customerId)
            .OrderByDescending(f => f.CreatedAt)
            .ThenByDescending(f => f.WorkerId)
            .ToListAsync(cancellationToken);
    }

    public async Task<FavoriteWorker> AddIfMissingAsync(int customerId, int workerId, DateTime createdAt, CancellationToken cancellationToken = default)
    {
        var existing = await FindAsync(customerId, workerId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var favorite = new FavoriteWorker
        {
            CustomerId = customerId,
            WorkerId = workerId,
            CreatedAt = createdAt
        };
        _context.FavoriteWorkers.Add(favorite);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);

            // Rows are read untracked and removed with ExecuteDelete, which the change tracker cannot see:
            // do not leave the inserted entity tracked, or a later add of the same pair would clash on the key.
            _context.Entry(favorite).State = EntityState.Detached;
            return favorite;
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // A concurrent request inserted the same pair first (double-click): hand back its row.
            _context.Entry(favorite).State = EntityState.Detached;
            return await FindAsync(customerId, workerId, cancellationToken)
                   ?? throw new InvalidOperationException("Favorite worker row vanished after a duplicate key error.", ex);
        }
    }

    public async Task RemoveAsync(int customerId, int workerId, CancellationToken cancellationToken = default)
    {
        await _context.FavoriteWorkers
            .Where(f => f.CustomerId == customerId && f.WorkerId == workerId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private Task<FavoriteWorker?> FindAsync(int customerId, int workerId, CancellationToken cancellationToken) =>
        _context.FavoriteWorkers
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.CustomerId == customerId && f.WorkerId == workerId, cancellationToken);

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: SqlPrimaryKeyViolation or SqlUniqueIndexViolation };
}
