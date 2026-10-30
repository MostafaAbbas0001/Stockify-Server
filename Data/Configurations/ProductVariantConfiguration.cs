namespace Stockify.Data;

public class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.Property(variant => variant.ImageUrl).HasMaxLength(2048);
        builder.Property(variant => variant.Price).HasPrecision(18, 2);
        builder.Property(variant => variant.CreatedAt).HasDefaultValueSql("NOW()");
        builder.Property(variant => variant.IsSellable).HasDefaultValue(true);
        builder.HasIndex(variant => variant.CreatedAt);
        builder.HasIndex(variant => new { variant.TenantId, variant.SKU }).IsUnique();
        builder.HasIndex(variant => new { variant.TenantId, variant.Barcode })
            .IsUnique()
            .HasFilter("\"Barcode\" IS NOT NULL");
        builder.HasOne(variant => variant.Sale).WithMany().HasForeignKey(variant => variant.SaleId).OnDelete(DeleteBehavior.Restrict);

        // The attribute values (Size = M, Colour = Red) that define this variant.
        builder.HasMany(variant => variant.AttributeValues)
            .WithMany(value => value.Variants)
            .UsingEntity<Dictionary<string, object>>(
                "ProductVariantAttributeValue",
                right => right.HasOne<ProductAttributeValue>().WithMany().HasForeignKey("ProductAttributeValueId").OnDelete(DeleteBehavior.Restrict),
                left => left.HasOne<ProductVariant>().WithMany().HasForeignKey("ProductVariantId").OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("ProductVariantAttributeValues");
                    join.HasKey("ProductVariantId", "ProductAttributeValueId");
                });
    }
}
