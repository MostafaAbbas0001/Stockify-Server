namespace Stockify.Models.Entities;

public class UserPermission : ITenantOwned
{
    public int TenantId { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;

    public bool IsActive { get; set; } = true;
}
