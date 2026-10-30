using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

public class ReceiveStockTransferLotRequest
{
    public required int StockTransferItemId { get; set; }
    public required int InventoryLotId { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Quantity cannot be negative")]
    public required int QuantityReceived { get; set; }
}
