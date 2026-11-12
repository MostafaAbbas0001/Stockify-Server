using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Stockify.Tests;

[Collection(nameof(ApiCollection))]
public class SmokeTests
{
    private readonly ApiFixture _api;

    public SmokeTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Owner_signs_in_and_reads_lookups()
    {
        var tenant = await _api.NewTenantAsync("Smoke Co");
        var lookups = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/lookups");
        Assert.True(lookups.GetProperty("paymentMethods").GetArrayLength() >= 3);
    }

    [Fact]
    public async Task A_tenant_cannot_read_another_tenants_customer()
    {
        var first = await _api.NewTenantAsync("First Co");
        var second = await _api.NewTenantAsync("Second Co");
        var secondWalkIn = await second.WalkInCustomerIdAsync();

        var response = await first.Owner.GetAsync($"/api/customers/{secondWalkIn}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Sale_deducts_stock_and_invoice_matches_price_plus_tax()
    {
        var tenant = await _api.NewTenantAsync("Sale Co");
        var (variantId, _, barcode) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 5);

        var (_, invoiceId) = await tenant.OrderAsync((variantId, 2));

        var invoice = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/invoices/{invoiceId}");
        Assert.Equal(22.20m, invoice.GetProperty("totalAmount").GetDecimal()); // 2 × 10.00 + 11 % tax
        var item = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/pos/items?code={barcode}");
        Assert.Equal(3, item.GetProperty("stockQuantity").GetInt32());
    }
}
