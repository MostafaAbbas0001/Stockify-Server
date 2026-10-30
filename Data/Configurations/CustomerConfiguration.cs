namespace Stockify.Data;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(customer => customer.CreatedAt).HasDefaultValueSql("NOW()");
        builder.HasIndex(customer => customer.CreatedAt);
    }
}
