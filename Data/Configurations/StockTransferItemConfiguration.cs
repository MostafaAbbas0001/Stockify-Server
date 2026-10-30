namespace Stockify.Data;

public class StockTransferItemConfiguration : IEntityTypeConfiguration<StockTransferItem>
{
    public void Configure(EntityTypeBuilder<StockTransferItem> builder)
    {
        builder.HasOne(sti => sti.ProductVariant).WithMany().HasForeignKey(sti => sti.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(sti => sti.QuantityRequested).HasDefaultValue(0);
        builder.Property(sti => sti.QuantitySent).HasDefaultValue(0);
        builder.Property(sti => sti.QuantityReceived).HasDefaultValue(0);
    }
}
