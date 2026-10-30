namespace Stockify.Models.Entities;

public class StockTransferItem : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    
    // Parent transfer
    public int StockTransferId { get; set; }
    public StockTransfer StockTransfer { get; set; } = null!;
    
    // Variant being transferred
    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;
    
    // CRITICAL: Separate quantities
    public int QuantityRequested { get; set; }  // Original request
    public int QuantitySent { get; set; }       // Actually sent by source
    public int QuantityReceived { get; set; }   // Actually received by destination
    
    // Optional: Track discrepancies
    public string? ReceivingNotes { get; set; }  // e.g., "5 damaged items"
    public ICollection<StockTransferItemLot> Lots { get; set; } = new List<StockTransferItemLot>();
}
