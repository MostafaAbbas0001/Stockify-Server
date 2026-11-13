namespace Stockify.Data;

public class CheckoutKeyConfiguration : IEntityTypeConfiguration<CheckoutKey>
{
    public void Configure(EntityTypeBuilder<CheckoutKey> builder)
    {
        builder.Property(k => k.Key).HasMaxLength(64).IsRequired();
        builder.Property(k => k.Change).HasPrecision(18, 2);
        // One sale per key per company: the database is the last line against duplicate sales.
        builder.HasIndex(k => new { k.TenantId, k.Key }).IsUnique();
        builder.HasOne(k => k.Order).WithMany().HasForeignKey(k => k.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}
