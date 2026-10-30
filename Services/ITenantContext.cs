namespace Stockify.Services;

/// <summary>
/// The tenant the current operation acts for. In a web request it comes from the JWT
/// TenantId claim; it is null for anonymous and platform-admin requests, which then
/// see no tenant data.
/// </summary>
public interface ITenantContext
{
    int? TenantId { get; }

    /// <summary>Acts as the given tenant until disposed (provisioning, sign-in, tests).</summary>
    IDisposable UseTenant(int tenantId);
}
