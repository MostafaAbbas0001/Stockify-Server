namespace Stockify.Services;

/// <summary>
/// Hands out per-tenant document numbers (ORD-1, INV-1, ...). The counter row is created
/// and incremented in one atomic PostgreSQL upsert, so concurrent requests never share a number.
/// </summary>
public class DocumentNumberService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;

    public DocumentNumberService(AppDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<string> NextAsync(string prefix)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new TenantAccessException("No tenant is selected for this operation.");

        // Not composable (INSERT ... RETURNING), so materialize without LINQ operators.
        var values = await _db.Database.SqlQuery<long>($"""
            INSERT INTO "TenantSequences" ("TenantId", "Name", "NextValue")
            VALUES ({tenantId}, {prefix}, 2)
            ON CONFLICT ("TenantId", "Name")
            DO UPDATE SET "NextValue" = "TenantSequences"."NextValue" + 1
            RETURNING "NextValue" - 1 AS "Value"
            """).ToListAsync();

        return $"{prefix}-{values.Single()}";
    }
}
