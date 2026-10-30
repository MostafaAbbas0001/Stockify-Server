using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Stockify.Data;

/// <summary>
/// Creates the first platform admin from PlatformAdmin:Email / PlatformAdmin:Password
/// (PlatformAdmin__Email / PlatformAdmin__Password in .env) when none exists yet.
/// </summary>
public static class PlatformAdminSeeder
{
    public static async Task EnsureBootstrapAdminAsync(IServiceProvider services, IConfiguration configuration)
    {
        var email = configuration["PlatformAdmin:Email"];
        var password = configuration["PlatformAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        using var scope = services.CreateScope();
        // "ServerLifecycle" is the log category Serilog shows at Information level.
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ServerLifecycle");

        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.PlatformAdmins.AnyAsync())
                return;

            var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
            db.PlatformAdmins.Add(new PlatformAdmin
            {
                Email = AuthService.NormalizeEmail(email),
                PasswordHash = hasher.Hash(password),
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Created bootstrap platform admin {Email}", email);
        }
        catch (Exception ex)
        {
            // A missing or unmigrated database must not stop the API from starting.
            logger.LogError(ex, "Could not create the bootstrap platform admin");
        }
    }
}
