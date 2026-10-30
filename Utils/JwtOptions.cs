using System.ComponentModel.DataAnnotations;

namespace Stockify.Utils;

/// <summary>Bound from the "JwtSettings" configuration section.</summary>
public class JwtOptions
{
    public const string SectionName = "JwtSettings";

    /// <summary>Signing key; at least 32 bytes. Set JwtSettings__Key in .env.</summary>
    [Required]
    [MinLength(32)]
    public string Key { get; set; } = null!;

    public string Issuer { get; set; } = "stockify";

    public string Audience { get; set; } = "stockify";

    /// <summary>Lifetime of a company user's access token.</summary>
    public int ExpiryMinutes { get; set; } = 180;

    /// <summary>Lifetime of a platform admin's access token (no refresh).</summary>
    public int PlatformExpiryMinutes { get; set; } = 60;

    public int RefreshExpiryDays { get; set; } = 7;
}
