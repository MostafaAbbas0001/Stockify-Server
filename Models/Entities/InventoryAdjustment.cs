namespace Stockify.Models.Entities;

public class InventoryAdjustment : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int InventoryLotId { get; set; }
    public InventoryLot InventoryLot { get; set; } = null!;
    public int QuantityBefore { get; set; }
    public int QuantityAfter { get; set; }
    public int DeltaQuantity { get; set; }
    public string Reason { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
