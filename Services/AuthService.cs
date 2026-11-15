namespace Stockify.Services;

/// <summary>Sign-in, token refresh and sign-out for company users.</summary>
public class AuthService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly TokenService _tokens;
    private readonly PasswordHasher _passwordHasher;

    public AuthService(
        AppDbContext db,
        ITenantContext tenantContext,
        TokenService tokens,
        PasswordHasher passwordHasher)
    {
        _db = db;
        _tenantContext = tenantContext;
        _tokens = tokens;
        _passwordHasher = passwordHasher;
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public async Task<object> LoginAsync(LoginRequest creds)
    {
        if (creds == null || string.IsNullOrWhiteSpace(creds.Email) || string.IsNullOrWhiteSpace(creds.Password))
            throw new ArgumentException("Email and password are required.");

        var email = NormalizeEmail(creds.Email);

        // No tenant is known before sign-in, so the lookup bypasses the tenant filter;
        // email is unique across all tenants.
        var user = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.AssignedRole)
            .Include(u => u.AssignedBranch)
            .Include(u => u.UserPermissions)
                .ThenInclude(up => up.Permission)
            .SingleOrDefaultAsync(u => u.Email == email);

        if (user == null || !_passwordHasher.Verify(creds.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid credentials");

        var tenant = await GetActiveTenantAsync(user.TenantId);

        using var tenantScope = _tenantContext.UseTenant(user.TenantId);
        return await IssueTokensAsync(user, tenant, replacedToken: null);
    }

    public async Task<object> RefreshAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException("Refresh token is required.");

        var tokenHash = _tokens.HashRefreshToken(refreshToken);
        var storedToken = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .Include(rt => rt.User)
                .ThenInclude(u => u.AssignedRole)
            .Include(rt => rt.User)
                .ThenInclude(u => u.AssignedBranch)
            .Include(rt => rt.User)
                .ThenInclude(u => u.UserPermissions)
                    .ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(rt => rt.Token == tokenHash);

        if (storedToken == null || !storedToken.IsActive)
            throw new UnauthorizedAccessException("Invalid refresh token");

        var user = storedToken.User;
        if (user == null)
            throw new KeyNotFoundException("User not found for this token.");

        var tenant = await GetActiveTenantAsync(user.TenantId);

        using var tenantScope = _tenantContext.UseTenant(user.TenantId);
        storedToken.RevokedAt = DateTime.UtcNow;
        return await IssueTokensAsync(user, tenant, replacedToken: storedToken.Token);
    }

    public async Task LogoutAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return;

        var tokenHash = _tokens.HashRefreshToken(refreshToken);
        var storedToken = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(rt => rt.Token == tokenHash);
        if (storedToken == null)
            return;

        using var tenantScope = _tenantContext.UseTenant(storedToken.TenantId);
        storedToken.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Changes the signed-in user's own password after checking the current one. Other devices are
    /// signed out (their refresh tokens are revoked); this session keeps working until its token expires.
    /// </summary>
    public async Task ChangePasswordAsync(int userId, ChangePasswordRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");
        // 400, not 401: a 401 would make the client think the session expired.
        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new ArgumentException("The current password is incorrect.");
        if (request.NewPassword == request.CurrentPassword)
            throw new ArgumentException("The new password must be different from the current one.");

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync();
        foreach (var token in tokens)
            token.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private async Task<Tenant> GetActiveTenantAsync(int tenantId)
    {
        var tenant = await _db.Tenants
            .AsNoTracking()
            .Include(t => t.Status)
            .FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant == null || tenant.Status.Code != TenantStatusCodes.Active)
            throw new TenantSuspendedException();
        return tenant;
    }

    private async Task<object> IssueTokensAsync(User user, Tenant tenant, string? replacedToken)
    {
        var permissions = await GetEffectivePermissionKeys(user);
        var accessToken = _tokens.CreateUserToken(new UserTokenClaims(
            user.Id,
            user.Username,
            user.Email,
            user.AssignedRole.Name,
            user.AssignedRole.Id,
            tenant.Id,
            tenant.Name,
            user.BranchId,
            user.AssignedBranch?.Name ?? "Unknown",
            permissions));

        var rawRefreshToken = _tokens.GenerateRefreshToken();
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = _tokens.HashRefreshToken(rawRefreshToken),
            ExpiresAt = DateTime.UtcNow.Add(_tokens.RefreshTokenLifetime),
            CreatedAt = DateTime.UtcNow,
            ReplacedByToken = replacedToken
        });
        await _db.SaveChangesAsync();

        return new
        {
            token = accessToken.Token,
            refreshToken = rawRefreshToken,
            expiresAt = accessToken.ExpiresAt
        };
    }

    private async Task<string[]> GetEffectivePermissionKeys(User user)
    {
        var activePermissionIds = user.UserPermissions
            .Where(up => up.IsActive)
            .Select(up => up.PermissionId)
            .ToHashSet();

        return await _db.Permissions
            .Where(p => activePermissionIds.Contains(p.Id))
            .Select(p => p.Key)
            .Distinct()
            .ToArrayAsync();
    }
}
