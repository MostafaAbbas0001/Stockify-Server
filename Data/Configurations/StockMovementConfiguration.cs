namespace Stockify.Data;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.HasOne(movement => movement.InventoryLot).WithMany().HasForeignKey(movement => movement.InventoryLotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(movement => movement.Branch).WithMany().HasForeignKey(movement => movement.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(movement => movement.ToBranch).WithMany().HasForeignKey(movement => movement.ToBranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(movement => movement.MovementType).WithMany().HasForeignKey(movement => movement.MovementTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(movement => movement.CreatedAt);
    }
}
