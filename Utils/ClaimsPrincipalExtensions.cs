using System.Security.Claims;

namespace Stockify.Utils;

/// <summary>Reads the signed-in user's identity, tenant, branch and permissions from the JWT.</summary>
public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal principal)
    {
        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("User ID not found in claims");
        }
        return userId;
    }

    public static int GetBranchId(this ClaimsPrincipal principal)
    {
        var branchIdClaim = principal.FindFirst(StockifyClaimTypes.BranchId)?.Value;
        if (string.IsNullOrEmpty(branchIdClaim) || !int.TryParse(branchIdClaim, out var branchId))
        {
            throw new UnauthorizedAccessException("Branch ID not found in claims");
        }
        return branchId;
    }

    public static int? FindTenantId(this ClaimsPrincipal principal)
    {
        var tenantIdClaim = principal.FindFirst(StockifyClaimTypes.TenantId)?.Value;
        return int.TryParse(tenantIdClaim, out var tenantId) ? tenantId : null;
    }

    public static bool IsPlatformAdmin(this ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true &&
        principal.HasClaim(StockifyClaimTypes.Scope, StockifyClaimTypes.PlatformScope);

    public static string GetUsername(this ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.Name)?.Value
            ?? throw new UnauthorizedAccessException("Username not found in claims");
    }

    /// <summary>Permission keys from the comma-separated "perms" claim.</summary>
    public static HashSet<string> GetPermissions(this ClaimsPrincipal principal)
    {
        var granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claim in principal.FindAll(StockifyClaimTypes.Permissions))
        {
            foreach (var key in claim.Value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                granted.Add(key.Trim());
        }
        return granted;
    }

    public static bool HasPermission(this ClaimsPrincipal principal, string permission) =>
        principal.GetPermissions().Contains(permission);
}
