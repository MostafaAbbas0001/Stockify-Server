using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Stockify.Tests;

/// <summary>Phase 6 feature: an end-of-day summary the owner can read in ten seconds.</summary>
[Collection(nameof(ApiCollection))]
public class EndOfDayTests
{
    private readonly ApiFixture _api;

    public EndOfDayTests(ApiFixture api) => _api = api;

    private static async Task<JsonElement> CheckoutAsync(TestTenant tenant, int variantId, int quantity, string method)
    {
        var response = await tenant.Owner.PostAsJsonAsync("/api/pos/checkout", new
        {
            idempotencyKey = Guid.NewGuid().ToString(),
            customerId = await tenant.WalkInCustomerIdAsync(),
            deliveryLocationId = await tenant.InStoreLocationIdAsync(),
            createdByEmployeeId = await tenant.SalespersonAsync(),
            discountTypeId = (int?)null,
            discountValue = 0,
            items = new[] { new { productVariantId = variantId, quantity } },
            payment = new { paymentMethodId = await tenant.LookupIdAsync("paymentMethods", method), currencyId = await tenant.BaseCurrencyIdAsync(), tendered = (decimal?)null, reference = "" },
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task End_of_day_totals_by_payment_method_with_refunds_and_cancels()
    {
        var tenant = await _api.NewTenantAsync("Z Report Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 40.00m, stock: 20);
        await CheckoutAsync(tenant, variantId, 1, "CASH");                 // 44.40 cash
        var card = await CheckoutAsync(tenant, variantId, 2, "CARD");      // 88.80 card
        var (cancelled, _) = await tenant.OrderAsync((variantId, 1));      // unpaid, then cancelled
        (await tenant.Owner.PostAsJsonAsync($"/api/orders/{cancelled}/cancel", new { reason = "walked out" })).EnsureSuccessStatusCode();
        var invoice = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/invoices/{card.GetProperty("invoiceId").GetInt32()}");
        var lineId = invoice.GetProperty("invoiceItems").EnumerateArray().First().GetProperty("id").GetInt32();
        (await tenant.Owner.PostAsJsonAsync($"/api/invoices/{card.GetProperty("invoiceId").GetInt32()}/returns", new
        {
            reason = "size", items = new[] { new { invoiceItemId = lineId, quantity = 1, reason = "size" } },
        })).EnsureSuccessStatusCode();                                     // 44.40 refunded

        var response = await tenant.Owner.GetAsync($"/api/reports/end-of-day?date={DateTime.UtcNow:yyyy-MM-dd}");

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var day = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, day.GetProperty("salesCount").GetInt32());
        Assert.Equal(120.00m, day.GetProperty("netSales").GetDecimal());
        Assert.Equal(13.20m, day.GetProperty("tax").GetDecimal());
        Assert.Equal(133.20m, day.GetProperty("total").GetDecimal());
        var payments = day.GetProperty("payments").EnumerateArray().ToDictionary(p => p.GetProperty("methodCode").GetString()!, p => p.GetProperty("amount").GetDecimal());
        Assert.Equal(44.40m, payments["CASH"]);
        Assert.Equal(88.80m, payments["CARD"]);
        Assert.Equal(44.40m, day.GetProperty("refunds").GetDecimal());
        Assert.Equal(1, day.GetProperty("cancelledCount").GetInt32());
        Assert.Equal(88.80m, day.GetProperty("netTakings").GetDecimal()); // payments − refunds
    }

    [Fact]
    public async Task Cashier_cannot_read_the_end_of_day_summary()
    {
        var tenant = await _api.NewTenantAsync("Z Report Perm Co");
        using var cashier = await tenant.UserAsync("PointOfSales", "pos.access", "order.create");

        var response = await cashier.GetAsync($"/api/reports/end-of-day?date={DateTime.UtcNow:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
