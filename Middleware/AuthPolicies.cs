namespace Stockify.Middleware;

public static class AuthPolicies
{
    /// <summary>Platform operator endpoints (tenant management).</summary>
    public const string Platform = "Platform";

    /// <summary>Rate limit applied to sign-in and token refresh.</summary>
    public const string SignInRateLimit = "SignIn";
}
