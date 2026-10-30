namespace Stockify.Data;

public class OrderItemLotConfiguration : IEntityTypeConfiguration<OrderItemLot>
{
    public void Configure(EntityTypeBuilder<OrderItemLot> builder)
    {
        // Which lots supplied each order line (FEFO), so returns restock the same lots.
        builder.HasKey(item => new { item.OrderItemId, item.InventoryLotId });
        builder.HasOne(item => item.OrderItem).WithMany(item => item.Lots).HasForeignKey(item => item.OrderItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.InventoryLot).WithMany().HasForeignKey(item => item.InventoryLotId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_OrderItemLots_Quantity",
            "\"Quantity\" > 0 AND \"ReturnedQuantity\" >= 0 AND \"ReturnedQuantity\" <= \"Quantity\""));
    }
}
