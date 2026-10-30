namespace Stockify.Models.Entities;

public class RefreshToken : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int UserId { get; set; }
    // SHA-256 hash of the token handed to the client; the raw token is never stored.
    public string Token { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByToken { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsActive => !IsRevoked && !IsExpired;

    public User User { get; set; } = null!;
}
