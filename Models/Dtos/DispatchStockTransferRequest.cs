namespace Stockify.Models.Dtos;

public class DispatchStockTransferRequest
{
    public required int SourceBranchId { get; set; }
    public string? Notes { get; set; }
    public List<DispatchStockTransferLotRequest> Lots { get; set; } = new();
}
