namespace Stockify.Models.Dtos;

public class DispatchStockTransferLotRequest
{
    public required int StockTransferItemId { get; set; }
    public required int InventoryLotId { get; set; }
    public required int Quantity { get; set; }
}
