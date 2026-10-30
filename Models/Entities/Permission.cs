namespace Stockify.Models.Entities;

public class Permission
{
    public int Id { get; set; }
    public string Key { get; set; } = null!;
    public string? Target { get; set; }

    public ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();
}
