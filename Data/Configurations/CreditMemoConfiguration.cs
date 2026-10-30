namespace Stockify.Data;

public class CreditMemoConfiguration : IEntityTypeConfiguration<CreditMemo>
{
    public void Configure(EntityTypeBuilder<CreditMemo> builder)
    {
        builder.Property(c => c.CreditMemoNumber).HasMaxLength(50);
        builder.HasIndex(c => new { c.TenantId, c.CreditMemoNumber }).IsUnique();
        builder.HasOne(cm => cm.Invoice).WithMany(i => i.CreditMemos).HasForeignKey(cm => cm.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
    }
}
