namespace Stockify.Data;

public class TaxRateConfiguration : IEntityTypeConfiguration<TaxRate>
{
    public void Configure(EntityTypeBuilder<TaxRate> builder)
    {
        builder.Property(x => x.Rate).HasPrecision(5, 4);
        // At most one default tax rate per tenant.
        builder.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasFilter("\"IsDefault\" = TRUE")
            .HasDatabaseName("IX_TaxRates_TenantId_Default");
    }
}
