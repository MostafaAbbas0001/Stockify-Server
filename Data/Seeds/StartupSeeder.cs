using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Stockify.Data;

/// <summary>
/// Runs every time the API starts and inserts the rows the system cannot work without, but only the
/// ones that are missing. Rows that already exist are left as they are, so it is safe to run on every start.
/// <list type="bullet">
/// <item>Global: every lookup row, the four roles, the permissions and each role's default permissions
/// (the same lists the migrations seed, from <see cref="LookupSeeds"/> and <see cref="IdentitySeeds"/>).</item>
/// <item>Per company: a branch, a default tax rate, a base currency, the in-store delivery location, the
/// walk-in customer, the expense category tree (<see cref="DefaultTenantData"/>) and a permission row for
/// every user and permission (active when the user's role has it by default, always active for the owner).</item>
/// </list>
/// The schema itself comes from the migrations; if any are pending, seeding is skipped with a warning.
/// </summary>
public static class StartupSeeder
{
    public const string WalkInCustomerName = "Walk-in Customer";

    public static async Task RunAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        // "ServerLifecycle" is the log category Serilog shows at Information level.
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ServerLifecycle");

        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count > 0)
            {
                logger.LogWarning(
                    "Startup seeding skipped: {Count} migration(s) not applied yet ({Migrations})",
                    pending.Count, string.Join(", ", pending));
                return;
            }

            var globalAdded = await SeedGlobalAsync(db, logger);
            if (globalAdded > 0)
                logger.LogInformation("Startup seeding added {Count} missing global row(s)", globalAdded);

            var tenants = await db.Tenants.AsNoTracking()
                .OrderBy(tenant => tenant.Id)
                .Select(tenant => new { tenant.Id, tenant.Name, tenant.OwnerUserId })
                .ToListAsync();
            foreach (var tenant in tenants)
            {
                if (tenant.OwnerUserId is null)
                    logger.LogWarning("Company {TenantId} ({Name}) has no owner user", tenant.Id, tenant.Name);

                try
                {
                    var added = await SeedTenantAsync(services, tenant.Id, tenant.OwnerUserId);
                    if (added.Count > 0)
                        logger.LogInformation(
                            "Startup seeding filled in for company {TenantId} ({Name}): {Added}",
                            tenant.Id, tenant.Name, string.Join("; ", added));
                }
                catch (Exception ex)
                {
                    // One broken company must not stop the others from being checked.
                    logger.LogError(ex, "Startup seeding failed for company {TenantId} ({Name})", tenant.Id, tenant.Name);
                }
            }
        }
        catch (Exception ex)
        {
            // A missing or unreachable database must not stop the API from starting.
            logger.LogError(ex, "Startup seeding failed");
        }
    }

    private static async Task<int> SeedGlobalAsync(AppDbContext db, ILogger logger)
    {
        var added = 0;

        var addLookups = typeof(StartupSeeder).GetMethod(nameof(AddMissingLookupsAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var rows in LookupSeeds.Lists())
        {
            // Each lookup list is its own table, so the generic helper is closed over the row type.
            var task = (Task<int>)addLookups.MakeGenericMethod(rows[0].GetType()).Invoke(null, new object[] { db, rows, logger })!;
            added += await task;
        }

        var roles = await db.Roles.AsNoTracking().Select(role => new { role.Id, role.Code }).ToListAsync();
        foreach (var role in IdentitySeeds.Roles())
        {
            if (roles.Any(existing => existing.Code == role.Code)) continue;
            if (roles.Any(existing => existing.Id == role.Id))
            {
                logger.LogWarning("Role {Code} is missing but its id {Id} is taken by another role", role.Code, role.Id);
                continue;
            }
            db.Roles.Add(role);
            added++;
        }

        var permissions = await db.Permissions.AsNoTracking().Select(permission => new { permission.Id, permission.Key }).ToListAsync();
        foreach (var permission in IdentitySeeds.Permissions())
        {
            if (permissions.Any(existing => existing.Key == permission.Key)) continue;
            if (permissions.Any(existing => existing.Id == permission.Id))
            {
                logger.LogWarning("Permission {Key} is missing but its id {Id} is taken by another permission", permission.Key, permission.Id);
                continue;
            }
            db.Permissions.Add(permission);
            added++;
        }

        // Roles and permissions first: the role defaults below point at them.
        await db.SaveChangesAsync();

        var roleIds = (await db.Roles.Select(role => role.Id).ToListAsync()).ToHashSet();
        var permissionIds = (await db.Permissions.Select(permission => permission.Id).ToListAsync()).ToHashSet();
        var defaults = (await db.RolePermissions.Select(rp => new { rp.RoleId, rp.PermissionId }).ToListAsync())
            .Select(rp => (rp.RoleId, rp.PermissionId))
            .ToHashSet();
        foreach (var rolePermission in IdentitySeeds.RolePermissions())
        {
            if (!roleIds.Contains(rolePermission.RoleId) || !permissionIds.Contains(rolePermission.PermissionId)) continue;
            if (defaults.Contains((rolePermission.RoleId, rolePermission.PermissionId))) continue;
            db.RolePermissions.Add(rolePermission);
            added++;
        }

        await db.SaveChangesAsync();
        return added;
    }

    private static async Task<int> AddMissingLookupsAsync<TLookup>(AppDbContext db, LookupEntity[] rows, ILogger logger)
        where TLookup : LookupEntity
    {
        var existing = await db.Set<TLookup>().AsNoTracking().Select(row => new { row.Id, row.Code }).ToListAsync();
        var added = 0;
        foreach (var row in rows.Cast<TLookup>())
        {
            if (existing.Any(item => item.Code == row.Code)) continue;
            if (existing.Any(item => item.Id == row.Id))
            {
                logger.LogWarning("{Table} row {Code} is missing but its id {Id} is taken by another row",
                    typeof(TLookup).Name, row.Code, row.Id);
                continue;
            }
            db.Set<TLookup>().Add(row);
            added++;
        }
        return added;
    }

    /// <summary>Fills in one company's missing defaults; returns what it added (empty when nothing was missing).</summary>
    private static async Task<List<string>> SeedTenantAsync(IServiceProvider services, int tenantId, int? ownerUserId)
    {
        // A scope per company keeps the change tracker small and the tenant override local.
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<ITenantContext>().UseTenant(tenantId);
        var added = new List<string>();

        if (!await db.Branches.AnyAsync())
        {
            db.Branches.Add(new Branch { Name = "Main Branch", CreatedAt = DateTime.UtcNow });
            added.Add("branch");
        }

        // Orders need the company's default tax rate.
        if (!await db.TaxRates.AnyAsync(rate => rate.IsDefault))
        {
            var firstRate = await db.TaxRates.OrderBy(rate => rate.Id).FirstOrDefaultAsync();
            if (firstRate is not null)
            {
                firstRate.IsDefault = true;
                added.Add($"tax rate '{firstRate.Name}' marked as default");
            }
            else
            {
                db.TaxRates.Add(new TaxRate { Name = "VAT", Rate = 0m, IsDefault = true });
                added.Add("default tax rate (VAT 0%)");
            }
        }

        // Payments need at least one currency; the first one is the base (rate 1).
        if (!await db.Currencies.AnyAsync())
        {
            db.Currencies.Add(new Currency { Code = "USD", Name = "US Dollar", Symbol = "$", ExchangeRate = 1m, IsActive = true });
            added.Add("base currency (USD)");
        }

        // POS sales go to the in-store location.
        if (!await db.DeliveryLocations.AnyAsync(location => location.IsInStore))
        {
            db.DeliveryLocations.Add(new DeliveryLocation
            {
                LocationName = "In Store",
                Fee = 0m,
                IsInStore = true,
                CreatedAt = DateTime.UtcNow
            });
            added.Add("in-store delivery location");
        }

        var walkInName = WalkInCustomerName.ToLower();
        if (!await db.Customers.AnyAsync(customer => customer.Name.ToLower() == walkInName))
        {
            db.Customers.Add(new Customer
            {
                Name = WalkInCustomerName,
                Phone = "-",
                Address = "In-store purchase",
                Email = "-",
                CreatedAt = DateTime.UtcNow
            });
            added.Add("walk-in customer");
        }

        // The expense category tree, matched by code; a missing parent is created before its children.
        var categories = (await db.ExpenseCategories.Where(category => category.Code != null).ToListAsync())
            .GroupBy(category => category.Code!)
            .ToDictionary(group => group.Key, group => group.First());
        var addedCategories = 0;
        foreach (var template in DefaultTenantData.ExpenseCategories)
        {
            if (categories.ContainsKey(template.Code)) continue;
            var category = new ExpenseCategory
            {
                Code = template.Code,
                Name = template.Name,
                Description = template.Description,
                Parent = template.ParentCode == null ? null : categories.GetValueOrDefault(template.ParentCode)
            };
            categories[template.Code] = category;
            db.ExpenseCategories.Add(category);
            addedCategories++;
        }
        if (addedCategories > 0)
            added.Add($"{addedCategories} expense categor{(addedCategories == 1 ? "y" : "ies")}");

        // Sign-in reads only UserPermissions, so a permission added after a user was created would
        // otherwise never reach that user. Missing rows follow the role's defaults; the owner gets all.
        var users = await db.Users.AsNoTracking().Select(user => new { user.Id, user.RoleId }).ToListAsync();
        if (users.Count > 0)
        {
            var permissionIds = await db.Permissions.AsNoTracking().Select(permission => permission.Id).ToListAsync();
            var roleDefaults = (await db.RolePermissions.AsNoTracking()
                    .Select(rp => new { rp.RoleId, rp.PermissionId })
                    .ToListAsync())
                .Select(rp => (rp.RoleId, rp.PermissionId))
                .ToHashSet();
            var existing = (await db.UserPermissions.AsNoTracking()
                    .Select(up => new { up.UserId, up.PermissionId })
                    .ToListAsync())
                .Select(up => (up.UserId, up.PermissionId))
                .ToHashSet();

            var addedPermissions = 0;
            foreach (var user in users)
            foreach (var permissionId in permissionIds)
            {
                if (existing.Contains((user.Id, permissionId))) continue;
                db.UserPermissions.Add(new UserPermission
                {
                    UserId = user.Id,
                    PermissionId = permissionId,
                    IsActive = user.Id == ownerUserId || roleDefaults.Contains((user.RoleId, permissionId))
                });
                addedPermissions++;
            }
            if (addedPermissions > 0)
                added.Add($"{addedPermissions} user permission row(s)");
        }

        if (added.Count > 0)
            await db.SaveChangesAsync();
        return added;
    }
}
