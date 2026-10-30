namespace Stockify.Utils;

/// <summary>The company account is suspended. Mapped to 403 with code TENANT_SUSPENDED.</summary>
public sealed class TenantSuspendedException : Exception
{
    public const string Code = "TENANT_SUSPENDED";

    public TenantSuspendedException()
        : base("This company account is not active. Contact support.") { }
}

/// <summary>An operation touched data of another tenant (or no tenant). Mapped to 403.</summary>
public sealed class TenantAccessException : Exception
{
    public TenantAccessException(string message) : base(message) { }
}
