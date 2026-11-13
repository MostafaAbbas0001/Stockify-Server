using System.Net.Http.Json;
using System.Text.Json;

namespace Stockify.Tests;

/// <summary>Audit A-03: any branchId of the company was accepted; only the dashboard and employees were scoped.</summary>
[Collection(nameof(ApiCollection))]
public class BranchScopeTests
{
    private readonly ApiFixture _api;

    public BranchScopeTests(ApiFixture api) => _api = api;

    private static readonly string[] CashierKeys =
    {
        "pos.access", "order.view", "order.create", "invoice.view", "invoice.payment.add", "customer.view",
        "product.view", "variant.view", "stock.view", "stockMovement.view",
    };

    private async Task<(TestTenant tenant, HttpClient mainCashier, int otherBranchId)> TwoBranchesWithASaleEachAsync()
    {
        var tenant = await _api.NewTenantAsync("Two Branch Co");
        var (variantId, lotId, _) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 5);
        (await tenant.Owner.PostAsJsonAsync("/api/branches", new { name = "Mall" })).EnsureSuccessStatusCode();
        var branches = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/branches");
        var rows = branches.ValueKind == JsonValueKind.Array ? branches.EnumerateArray() : branches.GetProperty("branches").EnumerateArray();
        var otherBranchId = rows.First(b => b.GetProperty("name").GetString() == "Mall").GetProperty("id").GetInt32();
        (await tenant.Owner.PostAsJsonAsync("/api/inventory-adjustments", new
        {
            variantId, branchId = otherBranchId, inventoryLotId = lotId, quantity = 5, reason = "stock the mall",
        })).EnsureSuccessStatusCode();

        await tenant.OrderAsync((variantId, 1)); // the owner works at the main branch
        var mallCashier = await tenant.UserAsync("PointOfSales", CashierKeys, otherBranchId);
        var mallSale = await mallCashier.PostAsJsonAsync("/api/orders", new
        {
            customerId = await tenant.WalkInCustomerIdAsync(),
            deliveryLocationId = await tenant.InStoreLocationIdAsync(),
            createdByEmployeeId = await tenant.SalespersonAsync(),
            discountTypeId = (int?)null,
            discountValue = 0,
            items = new[] { new { productVariantId = variantId, quantity = 1 } },
        });
        mallSale.EnsureSuccessStatusCode();

        var mainCashier = await tenant.UserAsync("PointOfSales", CashierKeys);
        return (tenant, mainCashier, otherBranchId);
    }

    [Theory]
    [InlineData("orders", "orders")]
    [InlineData("invoices", "invoices")]
    public async Task Staff_see_only_their_own_branch(string resource, string listKey)
    {
        var (tenant, mainCashier, otherBranchId) = await TwoBranchesWithASaleEachAsync();

        var own = await mainCashier.GetFromJsonAsync<JsonElement>($"/api/{resource}?page=1");
        var asked = await mainCashier.GetFromJsonAsync<JsonElement>($"/api/{resource}?page=1&branchId={otherBranchId}");
        var owner = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/{resource}?page=1");

        Assert.Equal(1, own.GetProperty(listKey).GetArrayLength());
        Assert.Equal(1, asked.GetProperty(listKey).GetArrayLength()); // the other branch is not reachable
        Assert.Equal(2, owner.GetProperty(listKey).GetArrayLength()); // branch.all.access sees both
    }

    [Fact]
    public async Task Staff_cannot_read_another_branchs_order_by_id()
    {
        var (tenant, mainCashier, otherBranchId) = await TwoBranchesWithASaleEachAsync();
        var all = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/orders?page=1&branchId={otherBranchId}");
        var mallOrderId = all.GetProperty("orders").EnumerateArray().First().GetProperty("id").GetInt32();

        var response = await mainCashier.GetAsync($"/api/orders/{mallOrderId}");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
