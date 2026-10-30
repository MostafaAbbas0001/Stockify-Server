namespace Stockify.Data;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.Property(employee => employee.CreatedAt).HasDefaultValueSql("NOW()");
        builder.HasIndex(employee => employee.CreatedAt);
    }
}
