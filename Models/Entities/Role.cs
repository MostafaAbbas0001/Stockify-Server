namespace Stockify.Models.Entities;

/// <summary>Global role template; its default permissions are in RolePermission.</summary>
public class Role
{
    public int Id { get; set; }

    /// <summary>Stable identifier, see RoleCodes.</summary>
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;
}
