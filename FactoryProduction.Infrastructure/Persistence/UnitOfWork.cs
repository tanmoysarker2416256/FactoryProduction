using System.Data;
using FactoryProduction.Application;
using Microsoft.EntityFrameworkCore;

namespace FactoryProduction.Infrastructure;

public sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, string? exclusiveLock, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            if (exclusiveLock is not null)
                await AcquireLockAsync(exclusiveLock, ct);

            var result = await work();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return result;
        }
        finally
        {
            // Disposing the transaction without Commit rolls it back; drop tracked entities either way.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Transaction-scoped SQL Server application lock: requests wait their turn, so two requests can never
    /// both pass the "does it exist yet?" check. The lock is released automatically on commit/rollback.
    /// </summary>
    private Task AcquireLockAsync(string name, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($@"
DECLARE @r int;
EXEC @r = sp_getapplock @Resource = {name}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
IF @r < 0 THROW 51000, 'Could not acquire the event-processing lock.', 1;", ct);
}
