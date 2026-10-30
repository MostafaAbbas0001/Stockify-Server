namespace Stockify.Data;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.Property(i => i.InvoiceNumber).HasMaxLength(50);
        builder.HasIndex(i => new { i.TenantId, i.InvoiceNumber }).IsUnique();
        builder.HasIndex(invoice => invoice.IssuedAt);
        builder.HasOne(i => i.TaxRate).WithMany(tr => tr.Invoices).HasForeignKey(i => i.TaxRateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.InvoiceStatus).WithMany().HasForeignKey(i => i.InvoiceStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.DiscountType).WithMany().HasForeignKey(i => i.DiscountTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Subtotal).HasPrecision(18, 2);
        builder.Property(x => x.TaxAmount).HasPrecision(18, 2);
        builder.Property(x => x.DeliveryChargeAmount).HasPrecision(18, 2);
        builder.Property(x => x.SaleAmount).HasPrecision(18, 2);
        builder.Property(x => x.DiscountAmount).HasPrecision(18, 2);
        builder.Property(x => x.DiscountValue).HasPrecision(18, 2);
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
    }
}
