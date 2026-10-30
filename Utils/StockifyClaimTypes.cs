namespace Stockify.Utils;

/// <summary>
/// Custom JWT claim names, shared by token creation (Infrastructure) and reading (Api).
/// PascalCase names avoid the JWT handler's inbound claim remapping (e.g. "tid").
/// </summary>
public static class StockifyClaimTypes
{
    public const string TenantId = "TenantId";
    public const string TenantName = "TenantName";
    public const string BranchId = "BranchId";
    public const string BranchName = "BranchName";
    public const string RoleId = "RoleId";
    public const string Permissions = "perms";
    public const string Scope = "Scope";
    public const string PlatformScope = "platform";
}
