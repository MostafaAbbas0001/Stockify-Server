using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

public class UpdateInventoryAdjustmentRequest
{
    public required int VariantId { get; set; }
    public required int BranchId { get; set; }
    public required int InventoryLotId { get; set; }

    [Range(-1000000, 1000000)]
    public int Quantity { get; set; }

    public required string Reason { get; set; }
}
