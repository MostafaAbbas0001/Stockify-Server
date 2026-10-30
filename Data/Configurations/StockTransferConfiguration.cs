namespace Stockify.Data;

public class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> builder)
    {
        builder.HasOne(st => st.SourceBranch).WithMany().HasForeignKey(st => st.SourceBranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(st => st.DestinationBranch).WithMany().HasForeignKey(st => st.DestinationBranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(st => st.CreatedBy).WithMany().HasForeignKey(st => st.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(st => st.Items).WithOne(sti => sti.StockTransfer).HasForeignKey(sti => sti.StockTransferId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(st => st.Status).WithMany().HasForeignKey(st => st.StatusId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(st => st.HasDiscrepancy).HasDefaultValue(false);
        builder.Property(st => st.IsResolved).HasDefaultValue(true);
        builder.Property(st => st.CreatedAt).HasColumnType("timestamp with time zone").HasDefaultValueSql("localtimestamp");
        builder.HasIndex(transfer => transfer.CreatedAt);
    }
}
