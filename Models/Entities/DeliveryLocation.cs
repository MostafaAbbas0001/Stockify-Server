namespace Stockify.Models.Entities;

public class DeliveryLocation : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public required string LocationName { get; set; }
    public required decimal Fee { get; set; }
    // The tenant's built-in "in store" location: orders sold at the counter, not delivered.
    public bool IsInStore { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}