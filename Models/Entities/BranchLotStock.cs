namespace Stockify.Models.Entities;

public class BranchLotStock : ITenantOwned
{
    public int TenantId { get; set; }
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int InventoryLotId { get; set; }
    public InventoryLot InventoryLot { get; set; } = null!;
    public int Quantity { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
