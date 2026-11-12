using System.Data;
using Npgsql;

namespace Stockify.Utils;

/// <summary>
/// Runs a read-check-write unit of work in a serializable transaction and retries it when PostgreSQL
/// reports a serialization failure (two tills paying the same invoice, two sales taking the last unit).
/// The body must re-read everything it checks, which every service method here already does.
/// </summary>
public static class SerializableTransactions
{
    private const int Attempts = 3;

    public static async Task<T> RunAsync<T>(AppDbContext db, Func<Task<T>> body)
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
            }
        }
    }

    /// <summary>40001 = serialization failure, 40P01 = deadlock detected.</summary>
    public static bool IsSerializationFailure(Exception ex) =>
        (ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState is "40001" or "40P01";
}
