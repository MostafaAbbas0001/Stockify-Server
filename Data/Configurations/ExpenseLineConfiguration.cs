namespace Stockify.Data;

public class ExpenseLineConfiguration : IEntityTypeConfiguration<ExpenseLine>
{
    public void Configure(EntityTypeBuilder<ExpenseLine> builder)
    {
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.UnitCost).HasPrecision(18, 2);
        builder.Property(x => x.ItemName).HasMaxLength(100);
        builder.Property(x => x.SupplierName).HasMaxLength(100);
        builder.Property(x => x.AdditionalInfo).HasMaxLength(500);
        builder.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Status).WithMany().HasForeignKey(x => x.StatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.LineType).WithMany().HasForeignKey(x => x.LineTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Payments).WithOne(p => p.ExpenseLine).HasForeignKey(p => p.ExpenseLineId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}
