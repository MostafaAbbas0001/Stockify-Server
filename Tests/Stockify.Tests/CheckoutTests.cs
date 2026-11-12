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
