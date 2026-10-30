namespace Stockify.Data;

public class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> builder)
    {
        builder.HasKey(up => new { up.UserId, up.PermissionId });
        builder.HasOne(up => up.User).WithMany(u => u.UserPermissions).HasForeignKey(up => up.UserId);
        builder.HasOne(up => up.Permission).WithMany(p => p.UserPermissions).HasForeignKey(up => up.PermissionId);
        builder.Property(up => up.IsActive).HasDefaultValue(true);
    }
}
