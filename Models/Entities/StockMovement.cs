namespace Stockify.Models.Entities;

public class StockMovement : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }

    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;
    public int? InventoryLotId { get; set; }
    public InventoryLot? InventoryLot { get; set; }

    public int MovementTypeId { get; set; }
    public MovementType MovementType { get; set; } = null!;

    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? Reason { get; set; }

    // Branch tracking for multi-branch inventory
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    // Optional: for inter-branch transfers
    public int? ToBranchId { get; set; }
    public Branch? ToBranch { get; set; }
}

