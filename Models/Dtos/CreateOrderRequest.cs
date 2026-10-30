namespace Stockify.Models.Dtos;

public class CreateOrderRequest
{
    public int CustomerId { get; set; }
    public int DeliveryLocationId { get; set; }
    public int CreatedByEmployeeId { get; set; }

    /// <summary>Id of a DiscountTypes row; null means no order-level discount.</summary>
    public int? DiscountTypeId { get; set; }

    public decimal DiscountValue { get; set; }
    public List<CreateOrderItemRequest> Items { get; set; } = new();
}
