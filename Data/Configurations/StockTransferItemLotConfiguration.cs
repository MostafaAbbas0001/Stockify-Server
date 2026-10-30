namespace Stockify.Data;

public class StockTransferItemLotConfiguration : IEntityTypeConfiguration<StockTransferItemLot>
{
    public void Configure(EntityTypeBuilder<StockTransferItemLot> builder)
    {
        builder.HasKey(item => new { item.StockTransferItemId, item.InventoryLotId });
        builder.HasOne(item => item.StockTransferItem).WithMany(item => item.Lots).HasForeignKey(item => item.StockTransferItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.InventoryLot).WithMany().HasForeignKey(item => item.InventoryLotId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_StockTransferItemLots_Quantity",
            "\"QuantitySent\" >= 0 AND \"QuantityReceived\" >= 0 AND \"QuantityReceived\" <= \"QuantitySent\""));
    }
}
