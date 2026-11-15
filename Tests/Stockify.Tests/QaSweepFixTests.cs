using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Stockify.Tests;

/// <summary>Fixes found by the browser QA sweep of 2026-11-15.</summary>
[Collection(nameof(ApiCollection))]
public class QaSweepFixTests
{
    private readonly ApiFixture _api;

    public QaSweepFixTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Any_user_can_change_their_own_password_with_the_current_one()
    {
        var tenant = await _api.NewTenantAsync("Password Co");
        var roles = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/users/roles");
        var roleId = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == "PointOfSales").GetProperty("id").GetInt32();
        var email = $"till-{Guid.NewGuid():N}@test.local";
        (await tenant.Owner.PostAsJsonAsync("/api/users", new { email, username = "Till", password = ApiFixture.Password, roleId, branchId = tenant.BranchId }))
            .EnsureSuccessStatusCode();
        using var cashier = _api.As(await _api.LoginAsync(email));

        var wrong = await cashier.PostAsJsonAsync("/api/account/password", new { currentPassword = "nope-nope", newPassword = "NewPass-2026" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var tooShort = await cashier.PostAsJsonAsync("/api/account/password", new { currentPassword = ApiFixture.Password, newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        var ok = await cashier.PostAsJsonAsync("/api/account/password", new { currentPassword = ApiFixture.Password, newPassword = "NewPass-2026" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _api.LoginAsync(email));
        Assert.False(string.IsNullOrEmpty(await _api.LoginAsync(email, "NewPass-2026")));
    }

    [Fact]
    public async Task Currencies_list_the_base_currency_first()
    {
        // LBP sorts before USD alphabetically; payment dialogs default to the first row.
        var tenant = await _api.NewTenantAsync("Currency Order Co", secondCurrencyRate: 89500m);
        var list = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/currencies");
        Assert.Equal("USD", list[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Brand_names_are_unique_ignoring_case_and_spaces()
    {
        var tenant = await _api.NewTenantAsync("Brand Dup Co");
        (await tenant.Owner.PostAsJsonAsync("/api/brands", new { name = "Northline" })).EnsureSuccessStatusCode();
        var duplicate = await tenant.Owner.PostAsJsonAsync("/api/brands", new { name = "  northline " });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Product_variant_list_includes_barcode_and_sellable_flag()
    {
        var tenant = await _api.NewTenantAsync("Variant List Co");
        var (variantId, _, barcode) = await tenant.VariantWithStockAsync(10m, 0);
        var products = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/products?page=1");
        var productId = products.GetProperty("products")[0].GetProperty("id").GetInt32();
        var rows = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/products/{productId}/variants");
        var row = rows.GetProperty("variants").EnumerateArray().First(v => v.GetProperty("id").GetInt32() == variantId);
        Assert.Equal(barcode, row.GetProperty("barcode").GetString());
        Assert.True(row.GetProperty("isSellable").GetBoolean());
    }

    [Fact]
    public async Task Branch_names_are_unique_ignoring_case()
    {
        var tenant = await _api.NewTenantAsync("Branch Dup Co");
        (await tenant.Owner.PostAsJsonAsync("/api/branches", new { name = "Outlet" })).EnsureSuccessStatusCode();
        var duplicate = await tenant.Owner.PostAsJsonAsync("/api/branches", new { name = " outlet " });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var rename = await tenant.Owner.PutAsJsonAsync($"/api/branches/{tenant.BranchId}", new { name = "OUTLET" });
        Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);
    }

    [Fact]
    public async Task Expense_with_plain_date_period_is_saved_as_utc()
    {
        // The expense form sends <input type="date"> values as "yyyy-MM-dd"; this used to be a 500.
        var tenant = await _api.NewTenantAsync("Expense Date Co");
        var parents = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/expenses/categories");
        var parentId = parents.EnumerateArray().First(c => c.GetProperty("code").GetString() == "4000").GetProperty("id").GetInt32();
        var children = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/expenses/categories?parentId={parentId}");
        var childId = children.EnumerateArray().First().GetProperty("id").GetInt32();
        var amountType = await tenant.LookupIdAsync("expenseLineTypes", "AMOUNT");
        var response = await tenant.Owner.PostAsJsonAsync("/api/expenses", new
        {
            expenseDate = DateTime.UtcNow, expenseCategoryId = parentId,
            lines = new[]
            {
                new { categoryId = childId, lineTypeId = amountType, amount = 300m, expensePeriodStart = "2026-11-01", expensePeriodEnd = "2026-11-30" },
            },
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }
}
