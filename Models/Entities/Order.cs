namespace Stockify.Models.Entities;

public class Order : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string OrderNumber { get; set; } = null!;

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public int OrderStatusId { get; set; }
    public OrderStatus OrderStatus { get; set; } = null!;

    public int OrderTypeId { get; set; }
    public OrderType OrderType { get; set; } = null!;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int? CreatedByEmployeeId { get; set; }
    public Employee? CreatedByEmployee { get; set; }

    public int DeliveryLocationId { get; set; }
    public DeliveryLocation DeliveryLocation { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ShippedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}