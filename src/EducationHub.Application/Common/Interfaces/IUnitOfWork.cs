using EducationHub.Domain.Common;

namespace EducationHub.Application.Common.Interfaces;

/// <summary>
/// Transactional boundary. Handlers mutate entities through repositories and then call
/// <see cref="SaveChangesAsync"/> exactly once, at the end, so a single request produces a
/// single unit of work.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside an explicit transaction, and is safe to use
    /// with the retrying execution strategy (a plain <c>BeginTransactionAsync</c> is not:
    /// the retrying strategy rejects user-initiated transactions unless the whole unit runs
    /// inside the strategy). Commits when the operation returns a successful Result, rolls
    /// back when it returns a failed one, and rolls back and rethrows on exception.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Generic read/write access to an aggregate. Deliberately exposes <see cref="Query"/> as
/// <see cref="IQueryable{TEntity}"/> so a handler can compose filters and includes but
/// cannot execute: materialization always happens through
/// <c>PaginationExtensions.ToPaginatedListAsync</c>, which forces async execution and
/// server-side paging.
/// </summary>
public interface IRepository<TEntity>
    where TEntity : class
{
    /// <summary>Tracked queryable. Filters declared on the DbContext are applied here.</summary>
    IQueryable<TEntity> Query { get; }

    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    void Update(TEntity entity);

    void Remove(TEntity entity);

    /// <summary>Stages a soft delete for entities implementing <see cref="ISoftDeletableEntity"/>.</summary>
    void SoftRemove(TEntity entity);
}
