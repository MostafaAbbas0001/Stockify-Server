namespace Stockify.Models.Entities;

public class Brand : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = null!;
}