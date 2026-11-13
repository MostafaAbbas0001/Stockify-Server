using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Stockify.Models.Entities;

namespace Stockify.Tests;

/// <summary>Findings of the phase 4 code review (docs/audit/REVIEW-phase4.md).</summary>
[Collection(nameof(ApiCollection))]
public class ReviewFixTests
{
    private readonly ApiFixture _api;

    public ReviewFixTests(ApiFixture api) => _api = api;

    private static async Task PayAsync(TestTenant tenant, int invoiceId, decimal amount)
    {
        var response = await tenant.Owner.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            amount, paymentMethodId = await tenant.LookupIdAsync("paymentMethods", "CASH"), reference = "", currencyId = await tenant.BaseCurrencyIdAsync(),
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<int> LineIdAsync(TestTenant tenant, int invoiceId, int variantId)
    {
        var invoice = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/invoices/{invoiceId}");
        return invoice.GetProperty("invoiceItems").EnumerateArray()
            .First(i => i.GetProperty("productVariantId").GetInt32() == variantId).GetProperty("id").GetInt32();
    }

    private static Task<HttpResponseMessage> ReturnAsync(TestTenant tenant, int invoiceId, int lineId, int quantity) =>
        tenant.Owner.PostAsJsonAsync($"/api/invoices/{invoiceId}/returns", new
        {
            reason = "test", items = new[] { new { invoiceItemId = lineId, quantity, reason = "test" } },
        });

    private static async Task<decimal> RefundOf(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("totalRefund").GetDecimal();
    }

    private static async Task<int> StockAsync(TestTenant tenant, string barcode) =>
        (await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/pos/items?code={barcode}")).GetProperty("stockQuantity").GetInt32();

    /// <summary>C1: returns split tax and discount by the gross share although both now sit on the net.</summary>
    [Fact]
    public async Task Each_returned_line_refunds_exactly_what_it_cost()
    {
        var tenant = await _api.NewTenantAsync("Refund Share Co");
        var (promoted, _, _) = await tenant.VariantWithStockAsync(price: 100.00m, stock: 5);
        var (plain, _, _) = await tenant.VariantWithStockAsync(price: 100.00m, stock: 5);
        await tenant.AssignSaleAsync(promoted, await tenant.SaleAsync(SaleTypeCodes.Percentage, 50m));
        // net 150, 10 % discount 15, taxable 135, tax 14.85, total 149.85
        var (_, invoiceId) = await tenant.OrderWithDiscountAsync(DiscountTypeCodes.Percent, 10m, (promoted, 1), (plain, 1));
        await PayAsync(tenant, invoiceId, 149.85m);

        var promotedRefund = await RefundOf(await ReturnAsync(tenant, invoiceId, await LineIdAsync(tenant, invoiceId, promoted), 1));
        var plainRefund = await RefundOf(await ReturnAsync(tenant, invoiceId, await LineIdAsync(tenant, invoiceId, plain), 1));

        Assert.Equal(49.95m, promotedRefund); // 50 − 5.00 discount + 4.95 tax
        Assert.Equal(99.90m, plainRefund);    // 100 − 10.00 discount + 9.90 tax
        var invoice = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/invoices/{invoiceId}");
        Assert.Equal("REFUNDED", invoice.GetProperty("statusCode").GetString());
    }

    /// <summary>I1: paying 1.00 and returning everything produced a full credit memo.</summary>
    [Fact]
    public async Task Refund_cannot_exceed_what_was_paid()
    {
        var tenant = await _api.NewTenantAsync("Refund Cap Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 5);
        var (_, invoiceId) = await tenant.OrderAsync((variantId, 1)); // 11.10
        await PayAsync(tenant, invoiceId, 1.00m);

        var response = await ReturnAsync(tenant, invoiceId, await LineIdAsync(tenant, invoiceId, variantId), 1);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>I3: two identical return submissions both passed their checks outside the transaction.</summary>
    [Fact]
    public async Task Concurrent_identical_returns_restock_once()
    {
        var tenant = await _api.NewTenantAsync("Return Race Co");
        var (variantId, _, barcode) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 5);
        var (_, invoiceId) = await tenant.OrderAsync((variantId, 1));
        await PayAsync(tenant, invoiceId, 11.10m);
        var lineId = await LineIdAsync(tenant, invoiceId, variantId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => ReturnAsync(tenant, invoiceId, lineId, 1)));

        Assert.Equal(1, responses.Count(r => r.IsSuccessStatusCode));
        Assert.Equal(5, await StockAsync(tenant, barcode));
    }

    /// <summary>I2: two cancels at once both restored the stock.</summary>
    [Fact]
    public async Task Concurrent_cancels_restock_once()
    {
        var tenant = await _api.NewTenantAsync("Cancel Race Co");
        var (variantId, _, barcode) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 5);
        var (orderId, _) = await tenant.OrderAsync((variantId, 2));

        var responses = await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => tenant.Owner.PostAsJsonAsync($"/api/orders/{orderId}/cancel", new { reason = "race" })));

        Assert.Equal(1, responses.Count(r => r.IsSuccessStatusCode));
        Assert.Equal(5, await StockAsync(tenant, barcode));
    }

    /// <summary>I4: a free sale could never be settled and stayed DRAFT / ISSUED.</summary>
    [Fact]
    public async Task Zero_total_checkout_is_fully_paid_without_a_payment()
    {
        var tenant = await _api.NewTenantAsync("Free Sale Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 0.40m, stock: 5);
        await tenant.AssignSaleAsync(variantId, await tenant.SaleAsync(SaleTypeCodes.FixedAmount, 0.50m));
        var body = new
        {
            idempotencyKey = Guid.NewGuid().ToString(),
            customerId = await tenant.WalkInCustomerIdAsync(),
            deliveryLocationId = await tenant.InStoreLocationIdAsync(),
            createdByEmployeeId = await tenant.SalespersonAsync(),
            discountTypeId = (int?)null,
            discountValue = 0,
            items = new[] { new { productVariantId = variantId, quantity = 1 } },
            payment = new { paymentMethodId = await tenant.LookupIdAsync("paymentMethods", "CASH"), currencyId = await tenant.BaseCurrencyIdAsync(), tendered = (decimal?)null, reference = "" },
        };

        var first = await tenant.Owner.PostAsJsonAsync("/api/pos/checkout", body);
        var replay = await tenant.Owner.PostAsJsonAsync("/api/pos/checkout", body);

        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        var result = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("fullyPaid").GetBoolean());
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fullyPaid").GetBoolean());
        var order = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/orders/{result.GetProperty("orderId").GetInt32()}");
        Assert.Equal("FULLY_PAID", order.GetProperty("statusCode").GetString());
    }

    /// <summary>I4: an invoice of one cent could not be paid because one cent counted as "settled".</summary>
    [Fact]
    public async Task One_cent_invoice_can_be_paid()
    {
        var tenant = await _api.NewTenantAsync("Cent Co", taxRate: 0m);
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 0.01m, stock: 5);
        var (_, invoiceId) = await tenant.OrderAsync((variantId, 1));

        await PayAsync(tenant, invoiceId, 0.01m);

        var invoice = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/invoices/{invoiceId}");
        Assert.Equal("FULLY_PAID", invoice.GetProperty("statusCode").GetString());
    }
}
