namespace Stockify.Models.Dtos;

/// <summary>What goes into a company user's access token.</summary>
public sealed record UserTokenClaims(
    int UserId,
    string Username,
    string Email,
    string RoleName,
    int RoleId,
    int TenantId,
    string TenantName,
    int BranchId,
    string BranchName,
    IReadOnlyCollection<string> Permissions);
