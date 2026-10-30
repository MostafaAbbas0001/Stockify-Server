namespace Stockify.Models.Entities;

public class Employee : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public bool IsSale { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
