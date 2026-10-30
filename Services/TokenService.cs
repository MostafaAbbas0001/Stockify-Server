using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Stockify.Services;

/// <summary>Issues HMAC-SHA256 signed JWTs and refresh tokens.</summary>
public class TokenService
{
    private readonly JwtOptions _options;

    public TokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshExpiryDays);

    public AccessToken CreateUserToken(UserTokenClaims user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.RoleName),
            new(StockifyClaimTypes.RoleId, user.RoleId.ToString()),
            new(StockifyClaimTypes.TenantId, user.TenantId.ToString()),
            new(StockifyClaimTypes.TenantName, user.TenantName),
            new(StockifyClaimTypes.BranchId, user.BranchId.ToString()),
            new(StockifyClaimTypes.BranchName, user.BranchName)
        };

        if (user.Permissions.Count > 0)
            claims.Add(new Claim(StockifyClaimTypes.Permissions, string.Join(",", user.Permissions)));

        return Write(claims, TimeSpan.FromMinutes(_options.ExpiryMinutes));
    }

    public AccessToken CreatePlatformToken(int adminId, string email) => Write(
        new[]
        {
            new Claim(ClaimTypes.NameIdentifier, adminId.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim(StockifyClaimTypes.Scope, StockifyClaimTypes.PlatformScope)
        },
        TimeSpan.FromMinutes(_options.PlatformExpiryMinutes));

    public string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    private AccessToken Write(IEnumerable<Claim> claims, TimeSpan lifetime)
    {
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var expires = DateTime.UtcNow.Add(lifetime);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
