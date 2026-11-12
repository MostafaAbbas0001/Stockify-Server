namespace Stockify.Models.Dtos;

/// <summary>A cart the POS wants priced before it is sold; same shape as an order minus the people.</summary>
public class CartPreviewRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();

    /// <summary>Id of a DiscountTypes row; null means no order-level discount.</summary>
    public int? DiscountTypeId { get; set; }

    public decimal DiscountValue { get; set; }

    /// <summary>Delivery location whose fee is added; null means in store.</summary>
    public int? DeliveryLocationId { get; set; }
}
