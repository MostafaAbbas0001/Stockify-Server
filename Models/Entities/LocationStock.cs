namespace Stockify.Models.Entities;

public class LocationStock : ITenantOwned
{
    public int TenantId { get; set; }
    // Branch relationship
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    // Product variant relationship
    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    // Branch-level inventory settings. Quantities are stored in BranchLotStock.
    public int ReorderLevel { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
