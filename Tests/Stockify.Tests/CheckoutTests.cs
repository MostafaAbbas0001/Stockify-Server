using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Stockify.Models.Entities;

namespace Stockify.Tests;

[Collection(nameof(ApiCollection))]
public class CheckoutTests
{
    private readonly ApiFixture _api;

    public CheckoutTests(ApiFixture api) => _api = api;

    /// <summary>
    /// Found in the launch load test: 20 tills selling different items at once lost 3 in 4 sales to
    /// "busy, try again", because every sale bumps the company's shared order and invoice counters.
    /// </summary>
    [Fact]
    public async Task Many_tills_selling_at_once_all_succeed()
    {
        var tenant = await _api.NewTenantAsync("Busy Shop Co");
        var variants = new List<int>();
        for (var i = 0; i < 15; i++)
            variants.Add((await tenant.VariantWithStockAsync(price: 10m, stock: 5)).variantId);
        var bodies = new List<object>();
        foreach (var variantId in variants)
            bodies.Add(await CheckoutBodyAsync(tenant, Guid.NewGuid().ToString(), variantId, 1, tendered: 100m));

        var results = await Task.WhenAll(bodies.Select(body => tenant.Owner.PostAsJsonAsync("/api/pos/checkout", body)));

        var failures = new List<string>();
        foreach (var r in results.Where(r => !r.IsSuccessStatusCode))
            failures.Add($"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        Assert.Empty(failures);
        var orders = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/orders?page=1");
        var numbers = orders.GetProperty("orders").EnumerateArray().Select(o => o.GetProperty("orderNumber").GetString()).ToList();
        Assert.Equal(15, numbers.Distinct().Count()); // every sale got its own number
    }

    /// <summary>Audit B-05: an oversized order discount produced a negative invoice.</summary>
    [Fact]
    public async Task Invoice_total_is_floored_at_zero_for_oversized_discount()
    {
        var tenant = await _api.NewTenantAsync("Floor Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 129.99m, stock: 5);
        await tenant.AssignSaleAsync(variantId, await tenant.SaleAsync(SaleTypeCodes.Percentage, 20m));

        var (_, invoiceId) = await tenant.OrderWithDiscountAsync(DiscountTypeCodes.Amount, 10000m, (variantId, 1));

        var invoice = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/invoices/{invoiceId}");
        Assert.Equal(129.99m, invoice.GetProperty("subtotal").GetDecimal());
        Assert.Equal(26.00m, invoice.GetProperty("saleAmount").GetDecimal());
        Assert.Equal(103.99m, invoice.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(0m, invoice.GetProperty("taxAmount").GetDecimal());
        Assert.Equal(0m, invoice.GetProperty("totalAmount").GetDecimal());
    }

    /// <summary>Audit L-02: the POS and the invoice priced the same cart differently (5.20 drift).</summary>
    [Fact]
    public async Task PosPreview_matches_invoice_for_promoted_item_with_order_discount()
    {
        var tenant = await _api.NewTenantAsync("Preview Co");
        var (puffer, _, _) = await tenant.VariantWithStockAsync(price: 129.99m, stock: 5);
        var (plain, _, _) = await tenant.VariantWithStockAsync(price: 40.00m, stock: 5);
        await tenant.AssignSaleAsync(puffer, await tenant.SaleAsync(SaleTypeCodes.Percentage, 20m));
        var percent = await tenant.LookupIdAsync("discountTypes", DiscountTypeCodes.Percent);
        var items = new[] { new { productVariantId = puffer, quantity = 2 }, new { productVariantId = plain, quantity = 1 } };

        var previewResponse = await tenant.Owner.PostAsJsonAsync("/api/pos/preview", new
        {
            items, discountTypeId = percent, discountValue = 10m, deliveryLocationId = await tenant.InStoreLocationIdAsync(),
        });
        previewResponse.EnsureSuccessStatusCode();
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        var (_, invoiceId) = await tenant.OrderWithDiscountAsync(DiscountTypeCodes.Percent, 10m, (puffer, 2), (plain, 1));
        var invoice = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/invoices/{invoiceId}");

        Assert.Equal(247.73m, preview.GetProperty("total").GetDecimal());
        Assert.Equal(invoice.GetProperty("totalAmount").GetDecimal(), preview.GetProperty("total").GetDecimal());
        Assert.Equal(invoice.GetProperty("taxAmount").GetDecimal(), preview.GetProperty("taxAmount").GetDecimal());
        Assert.Equal(invoice.GetProperty("discountAmount").GetDecimal(), preview.GetProperty("discountAmount").GetDecimal());
    }

    private static async Task<object> CheckoutBodyAsync(TestTenant tenant, string key, int variantId, int quantity, decimal? tendered, int? currencyId = null) => new
    {
        idempotencyKey = key,
        customerId = await tenant.WalkInCustomerIdAsync(),
        deliveryLocationId = await tenant.InStoreLocationIdAsync(),
        createdByEmployeeId = await tenant.SalespersonAsync(),
        discountTypeId = (int?)null,
        discountValue = 0,
        items = new[] { new { productVariantId = variantId, quantity } },
        payment = new
        {
            paymentMethodId = await tenant.LookupIdAsync("paymentMethods", "CASH"),
            currencyId = currencyId ?? await tenant.BaseCurrencyIdAsync(),
            tendered,
            reference = "",
        },
    };

    private static async Task<int> StockAsync(TestTenant tenant, string barcode) =>
        (await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/pos/items?code={barcode}")).GetProperty("stockQuantity").GetInt32();

    private static async Task<int> OrderCountAsync(TestTenant tenant) =>
        (await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/orders?page=1")).GetProperty("totalCount").GetInt32();

    /// <summary>Audit L-05 / I-02: the POS created the order and the payment in two calls.</summary>
    [Fact]
    public async Task Checkout_creates_order_and_payment_atomically()
    {
        var tenant = await _api.NewTenantAsync("Checkout Co");
        var (variantId, _, barcode) = await tenant.VariantWithStockAsync(price: 40.00m, stock: 5);

        var response = await tenant.Owner.PostAsJsonAsync("/api/pos/checkout", await CheckoutBodyAsync(tenant, Guid.NewGuid().ToString(), variantId, 1, tendered: 50.00m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(44.40m, result.GetProperty("total").GetDecimal());
        Assert.Equal(5.60m, result.GetProperty("change").GetDecimal());
        Assert.True(result.GetProperty("fullyPaid").GetBoolean());
        var order = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/orders/{result.GetProperty("orderId").GetInt32()}");
        Assert.Equal("FULLY_PAID", order.GetProperty("statusCode").GetString());
        Assert.Equal(4, await StockAsync(tenant, barcode));
    }

    /// <summary>Audit L-05: a retry or double-click created a second order and deducted stock twice.</summary>
    [Fact]
    public async Task Checkout_with_same_idempotency_key_returns_same_order()
    {
        var tenant = await _api.NewTenantAsync("Retry Co");
        var (variantId, _, barcode) = await tenant.VariantWithStockAsync(price: 40.00m, stock: 5);
        var body = await CheckoutBodyAsync(tenant, Guid.NewGuid().ToString(), variantId, 1, tendered: 44.40m);

        var first = await (await tenant.Owner.PostAsJsonAsync("/api/pos/checkout", body)).Content.ReadFromJsonAsync<JsonElement>();
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => tenant.Owner.PostAsJsonAsync("/api/pos/checkout", body)));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, $"retry returned {(int)r.StatusCode}"));
        foreach (var response in responses)
        {
            var again = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(first.GetProperty("orderId").GetInt32(), again.GetProperty("orderId").GetInt32());
        }
        Assert.Equal(1, await OrderCountAsync(tenant));
        Assert.Equal(4, await StockAsync(tenant, barcode));
    }

    /// <summary>Audit L-05: a failed payment left an unpaid order behind.</summary>
    [Fact]
    public async Task Checkout_with_failing_payment_creates_no_order()
    {
        var tenant = await _api.NewTenantAsync("Fail Co");
        var (variantId, _, barcode) = await tenant.VariantWithStockAsync(price: 40.00m, stock: 5);

        var response = await tenant.Owner.PostAsJsonAsync("/api/pos/checkout", await CheckoutBodyAsync(tenant, Guid.NewGuid().ToString(), variantId, 1, tendered: 50m, currencyId: 999999));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await OrderCountAsync(tenant));
        Assert.Equal(5, await StockAsync(tenant, barcode));
    }

    /// <summary>Audit B-09 / L-03: a free item was taxed and the scan preview went negative.</summary>
    [Fact]
    public async Task Free_item_costs_nothing_in_preview_and_invoice()
    {
        var tenant = await _api.NewTenantAsync("Free Co");
        var (variantId, _, barcode) = await tenant.VariantWithStockAsync(price: 0.40m, stock: 5);
        await tenant.AssignSaleAsync(variantId, await tenant.SaleAsync(SaleTypeCodes.FixedAmount, 0.50m));

        var scan = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/pos/items?code={barcode}");
        var (_, invoiceId) = await tenant.OrderAsync((variantId, 1));
        var invoice = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/invoices/{invoiceId}");

        Assert.Equal(0.40m, scan.GetProperty("saleDiscount").GetDecimal());
        Assert.Equal(0m, scan.GetProperty("subtotal").GetDecimal());
        Assert.Equal(0m, scan.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(0m, invoice.GetProperty("totalAmount").GetDecimal());
    }
}
