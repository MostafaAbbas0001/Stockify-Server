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

/// <summary>A branch-limited user named another branch of their company in a request. Mapped to 403.</summary>
public sealed class BranchAccessException : Exception
{
    public BranchAccessException(string message) : base(message) { }

    /// <summary>
    /// Throws unless <paramref name="branchScope"/> (from <c>User.BranchScope</c>; null = branch.all.access)
    /// allows <paramref name="branchId"/>.
    /// </summary>
    public static void Require(int? branchScope, int? branchId, string action)
    {
        if (branchScope != null && branchScope != branchId)
            throw new BranchAccessException($"You can only {action} at your own branch.");
    }
}
