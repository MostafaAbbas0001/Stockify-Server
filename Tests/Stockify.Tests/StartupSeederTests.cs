using Stockify.Data;

namespace Stockify.Tests;

/// <summary>The startup seeder puts back missing critical rows and leaves existing ones alone.</summary>
[Collection(nameof(ApiCollection))]
public class StartupSeederTests
{
    private readonly ApiFixture _api;

    public StartupSeederTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Startup_seeding_restores_missing_rows_once_and_keeps_existing_ones()
    {
        var tenant = await _api.NewTenantAsync("Seed Repair Co", taxRate: 0.11m);
        var t = ("t", (object)tenant.TenantId);
        var ownerId = await _api.ScalarAsync("SELECT \"OwnerUserId\" FROM \"Tenants\" WHERE \"Id\" = @t", t);

        // Break the company and the global data the way a partial import or a manual edit could.
        await _api.ExecuteSqlAsync("DELETE FROM \"Customers\" WHERE \"TenantId\" = @t", t);
        await _api.ExecuteSqlAsync("DELETE FROM \"DeliveryLocations\" WHERE \"TenantId\" = @t", t);
        await _api.ExecuteSqlAsync("UPDATE \"TaxRates\" SET \"IsDefault\" = false WHERE \"TenantId\" = @t", t);
        await _api.ExecuteSqlAsync("DELETE FROM \"ExpenseCategories\" WHERE \"TenantId\" = @t AND \"ParentId\" IS NOT NULL", t);
        await _api.ExecuteSqlAsync("DELETE FROM \"ExpenseCategories\" WHERE \"TenantId\" = @t AND \"Code\" = '4000'", t);
        await _api.ExecuteSqlAsync("DELETE FROM \"UserPermissions\" WHERE \"UserId\" = @u AND \"PermissionId\" = 29", ("u", ownerId));
        await _api.ExecuteSqlAsync("DELETE FROM \"RolePermissions\" WHERE \"RoleId\" = 4 AND \"PermissionId\" = 29");
        await _api.ExecuteSqlAsync("DELETE FROM \"BusinessTypes\" WHERE \"Code\" = 'OTHER'");

        // Twice: the second run must find nothing to add.
        await StartupSeeder.RunAsync(_api.Services);
        await StartupSeeder.RunAsync(_api.Services);

        Assert.Equal(1, await _api.ScalarAsync("SELECT COUNT(*) FROM \"Customers\" WHERE \"TenantId\" = @t AND \"Name\" = 'Walk-in Customer'", t));
        Assert.Equal(1, await _api.ScalarAsync("SELECT COUNT(*) FROM \"DeliveryLocations\" WHERE \"TenantId\" = @t AND \"IsInStore\"", t));

        // The existing 11% rate is promoted to default, not replaced by a new one.
        Assert.Equal(1, await _api.ScalarAsync("SELECT COUNT(*) FROM \"TaxRates\" WHERE \"TenantId\" = @t", t));
        Assert.Equal(1, await _api.ScalarAsync("SELECT COUNT(*) FROM \"TaxRates\" WHERE \"TenantId\" = @t AND \"IsDefault\" AND \"Rate\" = 0.11", t));

        Assert.Equal(DefaultTenantData.ExpenseCategories.Count,
            await _api.ScalarAsync("SELECT COUNT(*) FROM \"ExpenseCategories\" WHERE \"TenantId\" = @t", t));
        Assert.Equal(DefaultTenantData.ExpenseCategories.Count(c => c.ParentCode == "4000"), await _api.ScalarAsync(
            "SELECT COUNT(*) FROM \"ExpenseCategories\" c JOIN \"ExpenseCategories\" p ON p.\"Id\" = c.\"ParentId\" " +
            "WHERE c.\"TenantId\" = @t AND p.\"Code\" = '4000'", t));

        // The owner always gets a missing permission active.
        Assert.Equal(1, await _api.ScalarAsync(
            "SELECT COUNT(*) FROM \"UserPermissions\" WHERE \"UserId\" = @u AND \"PermissionId\" = 29 AND \"IsActive\"", ("u", ownerId)));

        Assert.Equal(1, await _api.ScalarAsync("SELECT COUNT(*) FROM \"RolePermissions\" WHERE \"RoleId\" = 4 AND \"PermissionId\" = 29"));
        Assert.Equal(1, await _api.ScalarAsync("SELECT COUNT(*) FROM \"BusinessTypes\" WHERE \"Code\" = 'OTHER' AND \"Id\" = 6"));

        // Rows that were already there are untouched: one currency, one branch.
        Assert.Equal(1, await _api.ScalarAsync("SELECT COUNT(*) FROM \"Currencies\" WHERE \"TenantId\" = @t", t));
        Assert.Equal(1, await _api.ScalarAsync("SELECT COUNT(*) FROM \"Branches\" WHERE \"TenantId\" = @t", t));
    }
}
