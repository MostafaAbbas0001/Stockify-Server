namespace Stockify.Data;

public class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.Property(x => x.Code).HasMaxLength(10);
        builder.Property(x => x.ExchangeRate).HasPrecision(18, 6);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}
