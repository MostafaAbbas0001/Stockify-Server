using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Stockify.Tests;

[Collection(nameof(ApiCollection))]
public class PaymentTests
{
    private readonly ApiFixture _api;

    public PaymentTests(ApiFixture api) => _api = api;

    private static async Task<HttpResponseMessage> PayAsync(TestTenant tenant, int invoiceId, decimal amount, string method = "CASH", int? currencyId = null, decimal? tendered = null) =>
        await tenant.Owner.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            amount,
            tendered,
            paymentMethodId = await tenant.LookupIdAsync("paymentMethods", method),
            reference = "",
            currencyId = currencyId ?? await tenant.BaseCurrencyIdAsync(),
        });

    private static async Task<JsonElement> InvoiceAsync(TestTenant tenant, int invoiceId) =>
        await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/invoices/{invoiceId}");

    /// <summary>Audit A-01 / A-10: negative, zero and sub-cent payments were stored.</summary>
    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(0.004)]
    public async Task Payment_amount_must_be_at_least_one_cent(decimal amount)
    {
        var tenant = await _api.NewTenantAsync("Pay Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 5);
        var (_, invoiceId) = await tenant.OrderAsync((variantId, 1));

        var response = await PayAsync(tenant, invoiceId, amount);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0m, (await InvoiceAsync(tenant, invoiceId)).GetProperty("paidAmount").GetDecimal());
    }

    [Fact]
    public async Task Full_payment_marks_invoice_and_order_fully_paid()
    {
        var tenant = await _api.NewTenantAsync("Paid Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 5);
        var (orderId, invoiceId) = await tenant.OrderAsync((variantId, 1));
        var total = (await InvoiceAsync(tenant, invoiceId)).GetProperty("totalAmount").GetDecimal();

        var response = await PayAsync(tenant, invoiceId, total);

        response.EnsureSuccessStatusCode();
        var order = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        Assert.Equal("FULLY_PAID", order.GetProperty("statusCode").GetString());
        Assert.Equal("FULLY_PAID", (await InvoiceAsync(tenant, invoiceId)).GetProperty("statusCode").GetString());
    }

    /// <summary>Audit B-02: a payment on a void invoice flipped a cancelled order back to FULLY_PAID.</summary>
    [Fact]
    public async Task Payment_on_void_invoice_is_rejected()
    {
        var tenant = await _api.NewTenantAsync("Void Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 5);
        var (orderId, invoiceId) = await tenant.OrderAsync((variantId, 1));
        (await tenant.Owner.PatchAsJsonAsync($"/api/orders/{orderId}/status", new { action = "cancel" })).EnsureSuccessStatusCode();

        var response = await PayAsync(tenant, invoiceId, 11.10m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var order = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        Assert.Equal("CANCELLED", order.GetProperty("statusCode").GetString());
        Assert.Equal("VOID", (await InvoiceAsync(tenant, invoiceId)).GetProperty("statusCode").GetString());
    }

    /// <summary>Audit B-01: three parallel full payments were all accepted.</summary>
    [Fact]
    public async Task Concurrent_payments_accept_only_one()
    {
        var tenant = await _api.NewTenantAsync("Race Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 40.00m, stock: 5);
        var (_, invoiceId) = await tenant.OrderAsync((variantId, 1));
        var total = (await InvoiceAsync(tenant, invoiceId)).GetProperty("totalAmount").GetDecimal();

        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => PayAsync(tenant, invoiceId, total)));

        Assert.Equal(1, responses.Count(r => r.IsSuccessStatusCode));
        Assert.Equal(total, (await InvoiceAsync(tenant, invoiceId)).GetProperty("paidAmount").GetDecimal());
    }

    /// <summary>D19: cash tendered above the amount due returns change instead of being rejected.</summary>
    [Fact]
    public async Task Cash_overpayment_returns_change()
    {
        var tenant = await _api.NewTenantAsync("Change Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 40.00m, stock: 5);
        var (_, invoiceId) = await tenant.OrderAsync((variantId, 1)); // 44.40

        var response = await PayAsync(tenant, invoiceId, amount: 44.40m, tendered: 50.00m);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5.60m, body.GetProperty("change").GetDecimal());
        var invoice = await InvoiceAsync(tenant, invoiceId);
        Assert.Equal("FULLY_PAID", invoice.GetProperty("statusCode").GetString());
        Assert.Equal(44.40m, invoice.GetProperty("paidAmount").GetDecimal());
    }

    /// <summary>Audit B-07: 993 000 LBP on an 11.10 USD invoice left 0.01 USD due forever.</summary>
    [Fact]
    public async Task Second_currency_rounding_settles_invoice()
    {
        var tenant = await _api.NewTenantAsync("LBP Co", secondCurrencyRate: 89500m);
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 5);
        var (_, invoiceId) = await tenant.OrderAsync((variantId, 1)); // 11.10 USD = 993 450 LBP
        var currencies = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/currencies");
        var lbp = currencies.EnumerateArray().First(c => c.GetProperty("code").GetString() == "LBP").GetProperty("id").GetInt32();

        var response = await PayAsync(tenant, invoiceId, amount: 993000m, currencyId: lbp);

        response.EnsureSuccessStatusCode();
        var invoice = await InvoiceAsync(tenant, invoiceId);
        Assert.Equal("FULLY_PAID", invoice.GetProperty("statusCode").GetString());
    }
}
