using System.Net.Http.Json;
using System.Text.Json;

namespace Stockify.Tests;

/// <summary>Phase 6 feature: which batches with stock expire in the next N days (supermarket, café).</summary>
[Collection(nameof(ApiCollection))]
public class ExpiringSoonTests
{
    private readonly ApiFixture _api;

    public ExpiringSoonTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Lists_lots_with_stock_that_expire_within_the_window_soonest_first()
    {
        var tenant = await _api.NewTenantAsync("Expiry List Co", businessType: "GROCERY");
        var (milk, milkLot, _) = await tenant.VariantWithStockAsync(price: 1.50m, stock: 12, name: "Milk");
        var (bread, breadLot, _) = await tenant.VariantWithStockAsync(price: 1.00m, stock: 6, name: "Bread");
        var (rice, riceLot, _) = await tenant.VariantWithStockAsync(price: 3.00m, stock: 9, name: "Rice");
        var (_, emptyLot, _) = await tenant.VariantWithStockAsync(price: 2.00m, stock: 0, name: "Yogurt");
        await _api.ExecuteSqlAsync("UPDATE \"InventoryLots\" SET \"ExpiryDate\" = CURRENT_DATE + 5 WHERE \"Id\" = @id", ("id", milkLot));
        await _api.ExecuteSqlAsync("UPDATE \"InventoryLots\" SET \"ExpiryDate\" = CURRENT_DATE + 1 WHERE \"Id\" = @id", ("id", breadLot));
        await _api.ExecuteSqlAsync("UPDATE \"InventoryLots\" SET \"ExpiryDate\" = CURRENT_DATE + 60 WHERE \"Id\" = @id", ("id", riceLot));
        await _api.ExecuteSqlAsync("UPDATE \"InventoryLots\" SET \"ExpiryDate\" = CURRENT_DATE + 2 WHERE \"Id\" = @id", ("id", emptyLot));

        var list = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/inventory-lots/expiring?days=7");

        var rows = list.EnumerateArray().ToList();
        Assert.Equal(new[] { "Bread", "Milk" }, rows.Select(r => r.GetProperty("productName").GetString()));
        Assert.Equal(6, rows[0].GetProperty("quantity").GetInt32());
        Assert.Equal(1, rows[0].GetProperty("daysLeft").GetInt32());
        _ = (milk, bread, rice);
    }
}
