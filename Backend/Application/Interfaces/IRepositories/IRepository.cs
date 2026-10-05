namespace CommonService.Application.Interfaces.IRepositories;

/// <summary>
/// Generic repository interface for an aggregate root.
/// </summary>
/// <typeparam name="TEntity">Aggregate entity type</typeparam>
/// <typeparam name="TId">Entity identifier type</typeparam>
public interface IRepository<TEntity, in TId> where TEntity : class
{
    Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TEntity>> ListAllAsync(CancellationToken cancellationToken = default);
    Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default);
    void Update(TEntity entity);
    void Delete(TEntity entity);
}
