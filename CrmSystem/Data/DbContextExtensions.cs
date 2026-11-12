using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Data;

public static class DbContextExtensions
{
    /// <summary>
    /// Runs several SaveChanges calls as ONE database transaction, compatible with
    /// EnableRetryOnFailure (the execution strategy owns the transaction).
    /// </summary>
    public static async Task<T> InTransactionAsync<T>(this ApplicationDbContext db, Func<Task<T>> action, CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await action();
            await tx.CommitAsync(ct);
            return result;
        });
    }

    public static Task InTransactionAsync(this ApplicationDbContext db, Func<Task> action, CancellationToken ct = default) =>
        db.InTransactionAsync(async () => { await action(); return true; }, ct);
}
