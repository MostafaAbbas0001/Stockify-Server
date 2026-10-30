namespace Stockify.Services;

/// <summary>Reads the tenant from the signed-in user's JWT; supports temporary overrides.</summary>
public sealed class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private int? _override;

    public TenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public int? TenantId => _override ?? _httpContextAccessor.HttpContext?.User.FindTenantId();

    public IDisposable UseTenant(int tenantId)
    {
        var previous = _override;
        _override = tenantId;
        return new Scope(() => _override = previous);
    }

    private sealed class Scope : IDisposable
    {
        private Action? _restore;

        public Scope(Action restore)
        {
            _restore = restore;
        }

        public void Dispose()
        {
            _restore?.Invoke();
            _restore = null;
        }
    }
}
