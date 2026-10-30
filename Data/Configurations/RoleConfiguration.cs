namespace Stockify.Data;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.Property(role => role.Code).IsRequired().HasMaxLength(50);
        builder.HasIndex(role => role.Code).IsUnique();
        builder.Property(role => role.Name).IsRequired().HasMaxLength(100);
    }
}
