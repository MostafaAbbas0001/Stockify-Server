namespace Stockify.Models.Entities;

/// <summary>A company using the platform. Not tenant-owned itself.</summary>
public class Tenant
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;

    public int BusinessTypeId { get; set; }
    public BusinessType BusinessType { get; set; } = null!;

    public int StatusId { get; set; }
    public TenantStatus Status { get; set; } = null!;

    public int? OwnerUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
