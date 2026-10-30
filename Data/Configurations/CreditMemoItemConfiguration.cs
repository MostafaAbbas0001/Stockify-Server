namespace Stockify.Data;

public class CreditMemoItemConfiguration : IEntityTypeConfiguration<CreditMemoItem>
{
    public void Configure(EntityTypeBuilder<CreditMemoItem> builder)
    {
        builder.HasOne(cmi => cmi.CreditMemo).WithMany(cm => cm.CreditMemoItems).HasForeignKey(cmi => cmi.CreditMemoId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(cmi => cmi.InvoiceItem).WithMany(il => il.CreditMemoItems).HasForeignKey(cmi => cmi.InvoiceItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(cmi => cmi.ProductVariant).WithMany(pv => pv.CreditMemoItems).HasForeignKey(cmi => cmi.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.LineGross).HasPrecision(18, 2);
        builder.Property(x => x.LineSale).HasPrecision(18, 2);
        builder.Property(x => x.LineNet).HasPrecision(18, 2);
        builder.Property(x => x.RefundAmount).HasPrecision(18, 2);
    }
}
