namespace Stockify.Data;

public class BranchLotStockConfiguration : IEntityTypeConfiguration<BranchLotStock>
{
    public void Configure(EntityTypeBuilder<BranchLotStock> builder)
    {
        // Quantity of one lot held at one branch: the source of truth for stock.
        builder.HasKey(stock => new { stock.BranchId, stock.InventoryLotId });
        builder.HasOne(stock => stock.Branch).WithMany().HasForeignKey(stock => stock.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(stock => stock.InventoryLot).WithMany(lot => lot.BranchStocks).HasForeignKey(stock => stock.InventoryLotId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(stock => stock.Quantity).HasDefaultValue(0);
        builder.ToTable(table => table.HasCheckConstraint("CK_BranchLotStocks_Quantity", "\"Quantity\" >= 0"));
        builder.HasIndex(stock => stock.InventoryLotId);
    }
}
