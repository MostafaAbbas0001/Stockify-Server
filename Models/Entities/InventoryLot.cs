namespace Stockify.Models.Entities;

public class InventoryLot : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;
    public string LotNumber { get; set; } = null!;
    public DateOnly? ManufacturingDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public ICollection<BranchLotStock> BranchStocks { get; set; } = new List<BranchLotStock>();
}
