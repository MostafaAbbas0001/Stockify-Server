using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Stockify.Utils;

/// <summary>
/// Runs a read-check-write unit of work in a serializable transaction and retries it when PostgreSQL
/// reports a serialization failure (two tills paying the same invoice, two sales taking the last unit).
/// The body must re-read everything it checks, which every service method here already does.
/// </summary>
public static class SerializableTransactions
{
    private const int Attempts = 5;

    /// <summary>High 32 bits of the advisory-lock key, so these locks never collide with others.</summary>
    private const long LockNamespace = 0x53544B59L << 32; // "STKY"

    public static async Task<T> RunAsync<T>(AppDbContext db, Func<Task<T>> body)
    {
        // Already inside a unit of work (e.g. checkout = order + payment): join it; the outer call retries.
        if (db.Database.CurrentTransaction != null)
            return await body();

        // Every sale, payment, return and cancel of a company bumps the same document counters or touches
        // the same stock rows; under SERIALIZABLE two of them at once abort instead of waiting. Taking a
        // per-company session lock *before* the transaction starts makes them queue for a few milliseconds
        // and each then runs on a fresh snapshot, so busy tills no longer lose sales to "try again".
        var lockKey = LockNamespace | (uint)db.CurrentTenantId;
        await db.Database.OpenConnectionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock({0})", lockKey);
            try
            {
                return await RunWithRetriesAsync(db, body);
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock({0})", lockKey);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<T> RunWithRetriesAsync<T>(AppDbContext db, Func<Task<T>> body)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var result = await body();
                await transaction.CommitAsync();
                return result;
            }
            catch (Exception ex) when (IsSerializationFailure(ex) && attempt < Attempts)
            {
                await transaction.RollbackAsync();
                db.ChangeTracker.Clear();
                // Conflicts that remain (another company's work sharing an index page) clear quickly.
                await Task.Delay(Random.Shared.Next(5, 25) * attempt);
            }
        }
    }

    /// <summary>40001 = serialization failure, 40P01 = deadlock detected.</summary>
    public static bool IsSerializationFailure(Exception ex) =>
        (ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState is "40001" or "40P01";
}
