namespace Stockify.Data;

public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.Property(sale => sale.Value).HasPrecision(18, 2);
        builder.Property(sale => sale.CreatedAt).HasDefaultValueSql("NOW()");
        builder.HasIndex(sale => sale.CreatedAt);
        builder.HasOne(sale => sale.SaleType).WithMany().HasForeignKey(sale => sale.SaleTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}
