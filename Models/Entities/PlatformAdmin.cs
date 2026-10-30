namespace Stockify.Models.Entities;

/// <summary>Operator of the SaaS platform; manages tenants and lives outside them.</summary>
public class PlatformAdmin
{
    public int Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
