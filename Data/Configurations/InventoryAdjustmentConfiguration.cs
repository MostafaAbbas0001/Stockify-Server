namespace Stockify.Data;

public class InventoryAdjustmentConfiguration : IEntityTypeConfiguration<InventoryAdjustment>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustment> builder)
    {
        builder.Property(adjustment => adjustment.Reason).HasMaxLength(500);
        builder.HasOne(adjustment => adjustment.Branch).WithMany().HasForeignKey(adjustment => adjustment.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(adjustment => adjustment.InventoryLot).WithMany().HasForeignKey(adjustment => adjustment.InventoryLotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(adjustment => adjustment.CreatedAt);
        builder.HasIndex(adjustment => new { adjustment.BranchId, adjustment.InventoryLotId });
    }
}
