namespace Stockify.Models.Entities;

public class StockTransferItemLot : ITenantOwned
{
    public int TenantId { get; set; }
    public int StockTransferItemId { get; set; }
    public StockTransferItem StockTransferItem { get; set; } = null!;
    public int InventoryLotId { get; set; }
    public InventoryLot InventoryLot { get; set; } = null!;
    public int QuantitySent { get; set; }
    public int QuantityReceived { get; set; }
}
