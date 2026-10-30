namespace Stockify.Models.Entities;

public class OrderItemLot : ITenantOwned
{
    public int TenantId { get; set; }
    public int OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;
    public int InventoryLotId { get; set; }
    public InventoryLot InventoryLot { get; set; } = null!;
    public int Quantity { get; set; }
    public int ReturnedQuantity { get; set; }
}
