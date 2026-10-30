namespace Stockify.Data;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        // Order numbers are assigned per tenant by DocumentNumberService (ORD-1, ORD-2, ...).
        builder.Property(o => o.OrderNumber).HasMaxLength(50);
        builder.HasIndex(o => new { o.TenantId, o.OrderNumber }).IsUnique();
        builder.HasIndex(order => order.CreatedAt);
        builder.HasOne(o => o.DeliveryLocation).WithMany().HasForeignKey(o => o.DeliveryLocationId);
        builder.HasOne(o => o.OrderType).WithMany().HasForeignKey(o => o.OrderTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.OrderStatus).WithMany().HasForeignKey(o => o.OrderStatusId).OnDelete(DeleteBehavior.Restrict);
    }
}
