namespace Stockify.Models.Entities;

    public class User : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    // Login identity, stored lowercase and unique across all tenants.
    public string Email { get; set; } = null!;
    // Display name; only needs to be meaningful inside the tenant.
    public string Username { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public int RoleId { get; set; }

    public int BranchId { get; set; }
    public Branch AssignedBranch { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Role AssignedRole { get; set; } = null!;
    public ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
