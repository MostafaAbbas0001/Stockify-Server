using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

public class CreateStockTransferRequest
{
    public int? SourceBranchId { get; set; }
    public required int DestinationBranchId { get; set; }
    public string? Notes { get; set; }

    [MinLength(1, ErrorMessage = "At least one item is required")]
    public required List<CreateStockTransferItemRequest> Items { get; set; }
}
