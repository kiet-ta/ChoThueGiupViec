using CommonService.Application.Interfaces.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Persistence.Repositories;

/// <summary>
/// Base EF Core repository implementation.
/// Module-specific repositories can inherit from this or implement IRepository directly.
/// </summary>
public class EfRepository<TEntity, TId> : IRepository<TEntity, TId> where TEntity : class
{
    protected readonly AppDbContext Context;
    protected readonly DbSet<TEntity> DbSet;

    public EfRepository(AppDbContext context)
    {
        Context = context;
        DbSet = context.Set<TEntity>();
    }

    public virtual async Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        return await DbSet.FindAsync([id], cancellationToken);
    }

    public virtual async Task<IReadOnlyList<TEntity>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet.ToListAsync(cancellationToken);
    }

    public virtual async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await DbSet.AddAsync(entity, cancellationToken);
        return entity;
    }

    public virtual void Update(TEntity entity)
    {
        DbSet.Update(entity);
    }

    public virtual void Delete(TEntity entity)
    {
        DbSet.Remove(entity);
    }
}
