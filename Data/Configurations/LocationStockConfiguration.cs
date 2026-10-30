namespace Stockify.Data;

public class LocationStockConfiguration : IEntityTypeConfiguration<LocationStock>
{
    public void Configure(EntityTypeBuilder<LocationStock> builder)
    {
        // Branch-level settings per variant (reorder level); quantities live in BranchLotStock.
        builder.HasKey(ls => new { ls.BranchId, ls.ProductVariantId });
        builder.HasOne(ls => ls.Branch).WithMany().HasForeignKey(ls => ls.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(ls => ls.ProductVariant).WithMany(pv => pv.LocationStocks).HasForeignKey(ls => ls.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(ls => ls.BranchId);
        builder.HasIndex(ls => ls.ProductVariantId);
        builder.Property(ls => ls.ReorderLevel).HasDefaultValue(0);
        builder.Property(ls => ls.LastUpdated).HasDefaultValueSql("localtimestamp");
    }
}
