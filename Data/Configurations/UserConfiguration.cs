namespace Stockify.Data;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(user => user.Email).IsRequired().HasMaxLength(256);
        // Email is the login identity, so it is unique across all tenants.
        builder.HasIndex(user => user.Email).IsUnique();
        builder.Property(user => user.CreatedAt).HasDefaultValueSql("NOW()");
        builder.HasIndex(user => user.CreatedAt);
        builder.HasOne(u => u.AssignedRole).WithMany().HasForeignKey(u => u.RoleId);
        builder.HasOne(u => u.AssignedBranch).WithMany().HasForeignKey(u => u.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(u => u.RefreshTokens).WithOne(rt => rt.User).HasForeignKey(rt => rt.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
