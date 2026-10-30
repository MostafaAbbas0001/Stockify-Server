namespace Stockify.Data;

public class DeliveryLocationConfiguration : IEntityTypeConfiguration<DeliveryLocation>
{
    public void Configure(EntityTypeBuilder<DeliveryLocation> builder)
    {
        builder.Property(x => x.Fee).HasPrecision(18, 2);
        // Exactly one built-in in-store location per tenant.
        builder.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasFilter("\"IsInStore\" = TRUE")
            .HasDatabaseName("IX_DeliveryLocations_TenantId_InStore");
    }
}
