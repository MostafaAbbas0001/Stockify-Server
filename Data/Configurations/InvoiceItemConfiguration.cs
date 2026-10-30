namespace Stockify.Data;

public class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> builder)
    {
        builder.Property(x => x.BaseUnitPrice).HasPrecision(18, 2);
        builder.Property(x => x.PerUnitSaleAmount).HasPrecision(18, 2);
        builder.Property(x => x.FinalUnitPrice).HasPrecision(18, 2);
        builder.Property(x => x.LineGross).HasPrecision(18, 2);
        builder.Property(x => x.LineSale).HasPrecision(18, 2);
        builder.Property(x => x.LineNet).HasPrecision(18, 2);
        builder.HasOne(x => x.Invoice).WithMany(i => i.InvoiceItems).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
    }
}
