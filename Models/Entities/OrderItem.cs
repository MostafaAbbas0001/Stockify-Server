namespace Stockify.Models.Entities;

public class OrderItem : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    public int Quantity { get; set; }
    public ICollection<OrderItemLot> Lots { get; set; } = new List<OrderItemLot>();
}
