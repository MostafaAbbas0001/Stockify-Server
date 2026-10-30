namespace Stockify.Services;

/// <summary>Sign-in for platform operators. Their tokens carry Scope=platform and no tenant.</summary>
public class PlatformAuthService
{
    private readonly AppDbContext _db;
    private readonly TokenService _tokens;
    private readonly PasswordHasher _passwordHasher;

    public PlatformAuthService(AppDbContext db, TokenService tokens, PasswordHasher passwordHasher)
    {
        _db = db;
        _tokens = tokens;
        _passwordHasher = passwordHasher;
    }

    public async Task<object> LoginAsync(LoginRequest creds)
    {
        if (creds == null || string.IsNullOrWhiteSpace(creds.Email) || string.IsNullOrWhiteSpace(creds.Password))
            throw new ArgumentException("Email and password are required.");

        var email = AuthService.NormalizeEmail(creds.Email);
        var admin = await _db.PlatformAdmins.AsNoTracking().FirstOrDefaultAsync(a => a.Email == email);
        if (admin == null || !_passwordHasher.Verify(creds.Password, admin.PasswordHash))
            throw new UnauthorizedAccessException("Invalid credentials");

        var token = _tokens.CreatePlatformToken(admin.Id, admin.Email);
        return new { token = token.Token, expiresAt = token.ExpiresAt };
    }
}
