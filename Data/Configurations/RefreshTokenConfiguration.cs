namespace Stockify.Data;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        // Stores a SHA-256 hash of the token, never the token itself.
        builder.Property(token => token.Token).HasMaxLength(128);
        builder.HasIndex(token => token.Token).IsUnique();
    }
}
