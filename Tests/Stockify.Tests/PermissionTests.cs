using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Stockify.Tests;

[Collection(nameof(ApiCollection))]
public class PermissionTests
{
    private readonly ApiFixture _api;

    public PermissionTests(ApiFixture api) => _api = api;

    private static readonly string[] CashierKeys =
    {
        "pos.access", "order.view", "order.create", "invoice.view", "invoice.payment.add", "customer.view", "product.view", "variant.view",
    };

    /// <summary>Audit A-02: cancelling needs order.cancel, not merely order.view.</summary>
    [Fact]
    public async Task Cashier_without_order_cancel_cannot_cancel_an_order()
    {
        var tenant = await _api.NewTenantAsync("Perm Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 5);
        var (orderId, _) = await tenant.OrderAsync((variantId, 1));
        using var cashier = await tenant.UserAsync("PointOfSales", CashierKeys);

        var response = await cashier.PostAsJsonAsync($"/api/orders/{orderId}/cancel", new { reason = "test" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task User_with_order_cancel_can_cancel()
    {
        var tenant = await _api.NewTenantAsync("Perm Cancel Co");
        var (variantId, _, barcode) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 5);
        var (orderId, _) = await tenant.OrderAsync((variantId, 2));
        using var manager = await tenant.UserAsync("OrderManager", CashierKeys.Append("order.cancel").ToArray());

        var response = await manager.PostAsJsonAsync($"/api/orders/{orderId}/cancel", new { reason = "wrong item" });

        response.EnsureSuccessStatusCode();
        var item = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/pos/items?code={barcode}");
        Assert.Equal(5, item.GetProperty("stockQuantity").GetInt32());
    }

    /// <summary>Audit A-05: returns were gated on invoice.create instead of invoice.return.</summary>
    [Fact]
    public async Task Return_needs_invoice_return_permission()
    {
        var tenant = await _api.NewTenantAsync("Perm Return Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 5);
        var (_, invoiceId) = await tenant.OrderAsync((variantId, 1));
        using var cashier = await tenant.UserAsync("PointOfSales", CashierKeys.Append("invoice.create").ToArray());

        var response = await cashier.PostAsJsonAsync($"/api/invoices/{invoiceId}/returns", new { reason = "x", items = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Audit A-04 / L-01: the cost price was returned to every role.</summary>
    [Fact]
    public async Task Cashier_cannot_see_cost_price()
    {
        var tenant = await _api.NewTenantAsync("Cost Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 5);
        using var cashier = await tenant.UserAsync("PointOfSales", CashierKeys);

        var list = await cashier.GetFromJsonAsync<JsonElement>("/api/product-variants?isSellable=true&page=1");
        var row = list.GetProperty("variants").EnumerateArray().First(v => v.GetProperty("id").GetInt32() == variantId);
        var ownerList = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/product-variants?isSellable=true&page=1");
        var ownerRow = ownerList.GetProperty("variants").EnumerateArray().First(v => v.GetProperty("id").GetInt32() == variantId);

        Assert.False(row.TryGetProperty("netPrice", out var hidden) && hidden.ValueKind == JsonValueKind.Number, "cost price leaked to the cashier");
        Assert.Equal(5.00m, ownerRow.GetProperty("netPrice").GetDecimal());
    }
}
