namespace Stockify.Data;

public class InventoryLotConfiguration : IEntityTypeConfiguration<InventoryLot>
{
    public void Configure(EntityTypeBuilder<InventoryLot> builder)
    {
        builder.Property(lot => lot.LotNumber).HasMaxLength(100);
        builder.Property(lot => lot.ManufacturingDate).HasColumnType("date");
        builder.Property(lot => lot.ExpiryDate).HasColumnType("date");
        builder.Property(lot => lot.ReceivedAt).HasColumnType("timestamp with time zone");
        builder.HasOne(lot => lot.ProductVariant).WithMany(variant => variant.InventoryLots).HasForeignKey(lot => lot.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(lot => new { lot.ProductVariantId, lot.LotNumber }).IsUnique();
        builder.HasIndex(lot => lot.ReceivedAt);
    }
}
