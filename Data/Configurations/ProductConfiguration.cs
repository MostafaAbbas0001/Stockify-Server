namespace Stockify.Data;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(product => product.ImageUrl).HasMaxLength(2048);
        builder.Property(product => product.CreatedAt).HasDefaultValueSql("NOW()");
        builder.HasIndex(product => product.CreatedAt);

        // Which attributes (Size, Colour, ...) the product's variants choose values for.
        builder.HasMany(product => product.Attributes)
            .WithMany(attribute => attribute.Products)
            .UsingEntity<Dictionary<string, object>>(
                "ProductAttributeAssignment",
                right => right.HasOne<ProductAttribute>().WithMany().HasForeignKey("ProductAttributeId").OnDelete(DeleteBehavior.Restrict),
                left => left.HasOne<Product>().WithMany().HasForeignKey("ProductId").OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("ProductAttributeAssignments");
                    join.HasKey("ProductId", "ProductAttributeId");
                });
    }
}
