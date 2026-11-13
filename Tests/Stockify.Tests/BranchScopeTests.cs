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

    /// <summary>Found in Phase 6: the POS tiles showed company-wide stock while the till sells only its own branch's.</summary>
    [Fact]
    public async Task Variant_list_counts_stock_of_the_users_branch()
    {
        var (tenant, mainCashier, otherBranchId) = await TwoBranchesWithASaleEachAsync();

        static int Stock(JsonElement list) =>
            list.GetProperty("variants").EnumerateArray().Single().GetProperty("stockQuantity").GetInt32();

        var cashier = await mainCashier.GetFromJsonAsync<JsonElement>("/api/product-variants?page=1");
        var cashierAsksMall = await mainCashier.GetFromJsonAsync<JsonElement>($"/api/product-variants?page=1&branchId={otherBranchId}");
        var ownerAll = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/product-variants?page=1");
        var ownerMall = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/product-variants?page=1&branchId={otherBranchId}");

        Assert.Equal(4, Stock(cashier));         // 5 received − 1 sold at the main branch
        Assert.Equal(4, Stock(cashierAsksMall)); // still the main branch: the mall is out of reach
        Assert.Equal(8, Stock(ownerAll));        // branch.all.access without a branch: the whole company
        Assert.Equal(4, Stock(ownerMall));
    }

    private static readonly string[] ManagerKeys = CashierKeys
        .Concat(new[] { "order.cancel", "invoice.return", "invoice.create", "stock.reduce", "stock.restock",
            "stockTransfer.request", "stockTransfer.send", "stockTransfer.receive", "stockTransfer.cancel",
            "stockTransfer.view", "stockTransfer.resolve" })
        .ToArray();

    private static async Task<int> IdOf(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("id", out var id) ? id.GetInt32() : body.GetProperty("orderId").GetInt32();
    }

    /// <summary>Backlog #22: lists and details were scoped, but actions by id were not.</summary>
    [Fact]
    public async Task Staff_cannot_act_on_another_branchs_order_or_invoice()
    {
        var (tenant, _, otherBranchId) = await TwoBranchesWithASaleEachAsync();
        var mall = (await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/orders?page=1&branchId={otherBranchId}"))
            .GetProperty("orders").EnumerateArray().Single();
        var mallOrderId = mall.GetProperty("id").GetInt32();
        var mallInvoiceId = (await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/orders/{mallOrderId}")).GetProperty("invoiceId").GetInt32();
        var invoice = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/invoices/{mallInvoiceId}");
        var lineId = invoice.GetProperty("invoiceItems").EnumerateArray().First().GetProperty("id").GetInt32();
        var currencyId = await tenant.BaseCurrencyIdAsync();
        var cash = await tenant.LookupIdAsync("paymentMethods", "CASH");
        var mainManager = await tenant.UserAsync("OrderManager", ManagerKeys);

        var ship = await mainManager.PatchAsJsonAsync($"/api/orders/{mallOrderId}/status", new { action = "ship" });
        var cancel = await mainManager.PostAsJsonAsync($"/api/orders/{mallOrderId}/cancel", new { reason = "not mine" });
        var pay = await mainManager.PostAsJsonAsync($"/api/invoices/{mallInvoiceId}/payments", new
        {
            amount = invoice.GetProperty("totalAmount").GetDecimal(), paymentMethodId = cash, currencyId, reference = "",
        });
        var refund = await mainManager.PostAsJsonAsync($"/api/invoices/{mallInvoiceId}/returns", new
        {
            reason = "not mine", items = new[] { new { invoiceItemId = lineId, quantity = 1, reason = "x" } },
        });

        Assert.Equal(System.Net.HttpStatusCode.NotFound, ship.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, cancel.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, pay.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, refund.StatusCode);
        var after = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/orders/{mallOrderId}");
        Assert.Equal("DRAFT", after.GetProperty("statusCode").GetString()); // nothing happened to it

        // Their own branch's order still works, and branch.all.access may act anywhere.
        var (ownVariantId, _, _) = await tenant.VariantWithStockAsync(price: 5m, stock: 3);
        var (ownOrderId, _) = await tenant.OrderAsync((ownVariantId, 1));
        Assert.Equal(System.Net.HttpStatusCode.OK, (await mainManager.PatchAsJsonAsync($"/api/orders/{ownOrderId}/status", new { action = "ship" })).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await tenant.Owner.PatchAsJsonAsync($"/api/orders/{mallOrderId}/status", new { action = "ship" })).StatusCode);
    }

    [Fact]
    public async Task Stock_adjustments_stay_in_the_users_branch()
    {
        var (tenant, _, otherBranchId) = await TwoBranchesWithASaleEachAsync();
        var (variantId, lotId, _) = await tenant.VariantWithStockAsync(price: 3m, stock: 4);
        var mallAdjustmentId = await IdOf(await tenant.Owner.PostAsJsonAsync("/api/inventory-adjustments", new
        {
            variantId, branchId = otherBranchId, inventoryLotId = lotId, quantity = 2, reason = "mall count",
        }));
        var mainManager = await tenant.UserAsync("OrderManager", ManagerKeys);
        var toMall = new { variantId, branchId = otherBranchId, inventoryLotId = lotId, quantity = 1, reason = "x" };

        var create = await mainManager.PostAsJsonAsync("/api/inventory-adjustments", toMall);
        var read = await mainManager.GetAsync($"/api/inventory-adjustments/{mallAdjustmentId}");
        var update = await mainManager.PutAsJsonAsync($"/api/inventory-adjustments/{mallAdjustmentId}", toMall);
        var delete = await mainManager.DeleteAsync($"/api/inventory-adjustments/{mallAdjustmentId}");
        var reorder = await mainManager.PutAsJsonAsync("/api/inventory-adjustments/reorder-level", new { variantId, branchId = otherBranchId, reorderLevel = 9 });
        var own = await mainManager.PostAsJsonAsync("/api/inventory-adjustments", new
        {
            variantId, branchId = tenant.BranchId, inventoryLotId = lotId, quantity = 1, reason = "own count",
        });

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, reorder.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Created, own.StatusCode);
    }

    [Fact]
    public async Task Transfers_are_handled_by_the_branches_at_each_end()
    {
        var (tenant, _, mallId) = await TwoBranchesWithASaleEachAsync();
        (await tenant.Owner.PostAsJsonAsync("/api/branches", new { name = "Airport" })).EnsureSuccessStatusCode();
        var branches = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/branches");
        var rows = branches.ValueKind == JsonValueKind.Array ? branches.EnumerateArray() : branches.GetProperty("branches").EnumerateArray();
        var airportId = rows.First(b => b.GetProperty("name").GetString() == "Airport").GetProperty("id").GetInt32();
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 3m, stock: 4);
        var items = new[] { new { productVariantId = variantId, quantity = 1 } };

        // Main → Mall, and a transfer between two other branches.
        var mainToMall = await IdOf(await tenant.Owner.PostAsJsonAsync("/api/stock-transfers", new { sourceBranchId = tenant.BranchId, destinationBranchId = mallId, items }));
        var mallToAirport = await IdOf(await tenant.Owner.PostAsJsonAsync("/api/stock-transfers", new { sourceBranchId = mallId, destinationBranchId = airportId, items }));
        var mainManager = await tenant.UserAsync("OrderManager", ManagerKeys);
        var mallManager = await tenant.UserAsync("OrderManager", ManagerKeys, mallId);

        var foreignRead = await mainManager.GetAsync($"/api/stock-transfers/{mallToAirport}");
        var foreignCancel = await mainManager.DeleteAsync($"/api/stock-transfers/{mallToAirport}?reason=x");
        var foreignCreate = await mainManager.PostAsJsonAsync("/api/stock-transfers", new { sourceBranchId = mallId, destinationBranchId = airportId, items });
        var destinationDispatches = await mallManager.PostAsJsonAsync($"/api/stock-transfers/{mainToMall}/dispatches", new
        {
            sourceBranchId = tenant.BranchId, lots = new[] { new { stockTransferItemId = 0, inventoryLotId = 0, quantity = 1 } },
        });
        var endRead = await mallManager.GetAsync($"/api/stock-transfers/{mainToMall}");
        var openRequest = await IdOf(await tenant.Owner.PostAsJsonAsync("/api/stock-transfers", new { destinationBranchId = airportId, items }));
        var openRead = await mainManager.GetAsync($"/api/stock-transfers/{openRequest}"); // any branch may fulfil it
        var openCancel = await mainManager.DeleteAsync($"/api/stock-transfers/{openRequest}?reason=x"); // only Airport may cancel
        var ownRequest = await mallManager.PostAsJsonAsync("/api/stock-transfers", new { sourceBranchId = tenant.BranchId, destinationBranchId = mallId, items });

        Assert.Equal(System.Net.HttpStatusCode.NotFound, foreignRead.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, foreignCancel.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, foreignCreate.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, destinationDispatches.StatusCode); // only Main may send it
        Assert.Equal(System.Net.HttpStatusCode.OK, endRead.StatusCode);
        Assert.True(ownRequest.IsSuccessStatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, openRead.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, openCancel.StatusCode);
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
