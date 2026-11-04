using EducationHub.Application.Common.Interfaces;
using EducationHub.Domain.Common;
using EducationHub.Infrastructure.Persistence;
using EducationHub.Shared.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Infrastructure.Persistence.Repositories;

/// <summary>
/// Generic EF Core repository.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Query"/> hands out the DbContext's own queryable, so the global query filters
/// that enforce tenant isolation are always in force. There is deliberately no
/// <c>IgnoreQueryFilters</c> option on this type - the only bypass in the codebase is
/// <c>AuthenticationRepository</c>, which needs it for a reason no query filter can express.
/// </para>
/// <para>
/// The concrete DbContext is injected rather than <c>IApplicationDbContext</c> because
/// <c>Set&lt;T&gt;</c> and <c>SaveChanges</c> are needed here; widening the interface to
/// expose them would leak persistence details into the Application layer.
/// </para>
/// </remarks>
public sealed class EfRepository<TEntity>(EducationHubDbContext context) : IRepository<TEntity>
    where TEntity : class
{
    private readonly DbSet<TEntity> _set = context.Set<TEntity>();

    public IQueryable<TEntity> Query => _set;

    /// <summary>
    /// Looks up by primary key through the filtered queryable.
    /// </summary>
    /// <remarks>
    /// <see cref="DbSet.FindAsync{TKey}"/> would be the obvious choice but is deliberately
    /// avoided: it checks the change tracker first, so an entity loaded before the tenant
    /// context was resolved would be returned by id regardless of tenant. Going through
    /// <see cref="Query"/> means another tenant's row reads as "not found" - there is
    /// nothing to leak and no second code path to reason about.
    /// </remarks>
    public Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _set.FirstOrDefaultAsync(e => EF.Property<Guid>(e, "Id") == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        => _set.AnyAsync(e => EF.Property<Guid>(e, "Id") == id, cancellationToken);

    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        await _set.AddAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public void Update(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        _set.Update(entity);
    }

    public void Remove(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        _set.Remove(entity);
    }

    /// <summary>
    /// Stages a soft delete.
    /// </summary>
    /// <remarks>
    /// Entities that do not implement <see cref="ISoftDeletableEntity"/> are hard-deleted
    /// rather than throwing. A hard delete on a type that never opted into soft deletion is
    /// the semantics its author asked for; failing loudly instead would mean adding an
    /// unrelated entity silently breaks every delete endpoint that touches it.
    /// <c>DeletedAt</c> and <c>DeletedById</c> are stamped by the DbContext on save, where
    /// the acting user is known.
    /// </remarks>
    public void SoftRemove(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity is ISoftDeletableEntity softDeletable)
        {
            softDeletable.IsDeleted = true;
        }
        else
        {
            _set.Remove(entity);
        }
    }
}

/// <summary>
/// Wraps the DbContext as the Application layer's unit of work.
/// </summary>
public sealed class UnitOfWork(EducationHubDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        // Deliberately the DbContext override, not base: that override is what stamps
        // audit/tenant fields and writes the audit rows, and calling the base directly would
        // silently disable both guarantees.
        => context.SaveChangesAsync(cancellationToken);

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        cancellationToken.ThrowIfCancellationRequested();

        // The retrying execution strategy does not support user-initiated transactions
        // unless the whole unit runs inside the strategy, so the operation executes as one
        // retriable block. On a transient failure the transaction is rolled back and EF
        // retries the operation against a fresh transaction.
        var strategy = context.Database.CreateExecutionStrategy();

        Func<object?, CancellationToken, Task<TResult>> run = async (_, ct) =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(ct)
                .ConfigureAwait(false);

            var result = await operation(ct).ConfigureAwait(false);

            // A failed Result is a business outcome, not an exception, yet it must still
            // undo the transaction - otherwise a refused administrator would leave a tenant
            // behind with no one to log in.
            if (result is Result failed && !failed.Succeeded)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
            }
            else
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }

            return result;
        };

        return await strategy
            .ExecuteAsync<object?, TResult>(state: null, operation: run, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
