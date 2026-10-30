namespace Stockify.Models.Dtos;

public class CreateOrderItemRequest
{
    public int ProductVariantId { get; set; }
    public int Quantity { get; set; }
}
