namespace Stockify.Models.Dtos;

public class ReceiveStockTransferRequest
{
    public string? Notes { get; set; }
    public bool Complete { get; set; }
    public required List<ReceiveStockTransferLotRequest> Lots { get; set; }
}
