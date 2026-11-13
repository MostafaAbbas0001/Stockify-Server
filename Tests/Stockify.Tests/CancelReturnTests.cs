using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Stockify.Tests;

[Collection(nameof(ApiCollection))]
public class CancelReturnTests
{
    private readonly ApiFixture _api;

    public CancelReturnTests(ApiFixture api) => _api = api;

    private static async Task PayAsync(TestTenant tenant, int invoiceId, decimal amount)
    {
        var response = await tenant.Owner.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            amount, paymentMethodId = await tenant.LookupIdAsync("paymentMethods", "CASH"), reference = "", currencyId = await tenant.BaseCurrencyIdAsync(),
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> ReturnAsync(TestTenant tenant, int invoiceId, int quantity)
    {
        var invoice = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/invoices/{invoiceId}");
        var lineId = invoice.GetProperty("invoiceItems").EnumerateArray().First().GetProperty("id").GetInt32();
        return await tenant.Owner.PostAsJsonAsync($"/api/invoices/{invoiceId}/returns", new
        {
            reason = "test", items = new[] { new { invoiceItemId = lineId, quantity, reason = "test" } },
        });
    }

    private static Task<HttpResponseMessage> CancelAsync(TestTenant tenant, int orderId) =>
        tenant.Owner.PostAsJsonAsync($"/api/orders/{orderId}/cancel", new { reason = "customer left" });

    private static async Task<int> StockAsync(TestTenant tenant, string barcode) =>
        (await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/pos/items?code={barcode}")).GetProperty("stockQuantity").GetInt32();

    /// <summary>Audit B-03: cancelling a part-paid order kept the money with no refund.</summary>
    [Fact]
    public async Task Cancel_is_refused_once_paid()
    {
        var tenant = await _api.NewTenantAsync("Cancel Paid Co");
        var (variantId, _, barcode) = await tenant.VariantWithStockAsync(price: 40.00m, stock: 10);
        var (orderId, invoiceId) = await tenant.OrderAsync((variantId, 1));
        await PayAsync(tenant, invoiceId, 22.20m);

        var response = await CancelAsync(tenant, orderId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(9, await StockAsync(tenant, barcode));
        var order = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        Assert.Equal("PARTIALLY_PAID", order.GetProperty("statusCode").GetString());
    }

    /// <summary>Audit B-04: cancel after a return restocked the returned units again (10 → 12).</summary>
    [Fact]
    public async Task Cancel_is_refused_after_a_return()
    {
        var tenant = await _api.NewTenantAsync("Cancel Returned Co");
        var (variantId, _, barcode) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 10);
        var (orderId, invoiceId) = await tenant.OrderAsync((variantId, 2));
        await PayAsync(tenant, invoiceId, 22.20m);
        (await ReturnAsync(tenant, invoiceId, 1)).EnsureSuccessStatusCode();
        Assert.Equal(9, await StockAsync(tenant, barcode));

        var response = await CancelAsync(tenant, orderId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(9, await StockAsync(tenant, barcode));
    }

    /// <summary>Audit B-06: a return on an unpaid invoice produced a credit memo and REFUNDED status.</summary>
    [Fact]
    public async Task Return_requires_a_payment()
    {
        var tenant = await _api.NewTenantAsync("Return Unpaid Co");
        var (variantId, _, barcode) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 10);
        var (_, invoiceId) = await tenant.OrderAsync((variantId, 1));

        var response = await ReturnAsync(tenant, invoiceId, 1);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(9, await StockAsync(tenant, barcode));
        var invoice = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/invoices/{invoiceId}");
        Assert.Equal("ISSUED", invoice.GetProperty("statusCode").GetString());
    }

    [Fact]
    public async Task Paid_order_is_reversed_through_a_return_and_stock_comes_back_once()
    {
        var tenant = await _api.NewTenantAsync("Return Paid Co");
        var (variantId, _, barcode) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 10);
        var (orderId, invoiceId) = await tenant.OrderAsync((variantId, 2));
        await PayAsync(tenant, invoiceId, 22.20m);

        (await ReturnAsync(tenant, invoiceId, 2)).EnsureSuccessStatusCode();

        Assert.Equal(10, await StockAsync(tenant, barcode));
        var order = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        Assert.Equal("RETURNED", order.GetProperty("statusCode").GetString());
        var invoice = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/invoices/{invoiceId}");
        Assert.Equal("REFUNDED", invoice.GetProperty("statusCode").GetString());
        Assert.Equal(22.20m, invoice.GetProperty("creditMemos").EnumerateArray().Sum(c => c.GetProperty("totalAmount").GetDecimal()));
    }

    [Fact]
    public async Task Cancel_reason_is_kept_on_the_order()
    {
        var tenant = await _api.NewTenantAsync("Cancel Reason Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 10);
        var (orderId, _) = await tenant.OrderAsync((variantId, 1));

        (await CancelAsync(tenant, orderId)).EnsureSuccessStatusCode();

        var order = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        Assert.Equal("CANCELLED", order.GetProperty("statusCode").GetString());
        Assert.Equal("customer left", order.GetProperty("cancelReason").GetString());
    }
}
