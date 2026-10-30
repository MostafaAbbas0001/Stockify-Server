using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

public class CreateStockTransferItemRequest
{
    public required int ProductVariantId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1")]
    public required int Quantity { get; set; }
}
