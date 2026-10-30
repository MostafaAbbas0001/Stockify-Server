using Microsoft.Extensions.Caching.Memory;

namespace Stockify.Services;

/// <summary>
/// Answers "may this tenant use the platform right now?" on every request.
/// Status is cached briefly; changing a tenant's status invalidates its entry.
/// </summary>
public class TenantStatusService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public TenantStatusService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<bool> IsActiveAsync(int tenantId)
    {
        var statusCode = await _cache.GetOrCreateAsync(CacheKey(tenantId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _db.Tenants
                .AsNoTracking()
                .Where(tenant => tenant.Id == tenantId)
                .Select(tenant => tenant.Status.Code)
                .FirstOrDefaultAsync();
        });
        return statusCode == TenantStatusCodes.Active;
    }

    public void Invalidate(int tenantId) => _cache.Remove(CacheKey(tenantId));

    private static string CacheKey(int tenantId) => $"tenant-status:{tenantId}";
}
