using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Stockify.Tests;

/// <summary>Brands, categories, attributes, products, variants (SKU rules, deletes) and promotions.</summary>
[Collection(nameof(ApiCollection))]
public class CatalogTests
{
    private readonly ApiFixture _api;

    public CatalogTests(ApiFixture api) => _api = api;

    private sealed record Catalog(TestTenant Tenant, int BrandId, int CategoryId, int AttributeId, int BlackId, int NavyId, int ProductId);

    /// <summary>Brand "Acme", category "Jackets", attribute Color {Black, Navy}, product "Field Coat" with Color.</summary>
    private async Task<Catalog> CatalogAsync()
    {
        var tenant = await _api.NewTenantAsync("Catalog Co");
        var o = tenant.Owner;
        var brandId = await TestTenant.IdOf(await o.PostAsJsonAsync("/api/brands", new { name = "Acme" }));
        var categoryId = await TestTenant.IdOf(await o.PostAsJsonAsync("/api/categories", new { name = "Jackets" }));
        var attributeId = await TestTenant.IdOf(await o.PostAsJsonAsync("/api/attributes", new { name = "Color", values = new[] { "Black", "Navy" } }));
        var attribute = await o.GetFromJsonAsync<JsonElement>($"/api/attributes/{attributeId}");
        int ValueId(string v) => attribute.GetProperty("values").EnumerateArray().First(x => x.GetProperty("value").GetString() == v).GetProperty("id").GetInt32();
        var productId = await TestTenant.IdOf(await o.PostAsJsonAsync("/api/products", new { name = "Field Coat", brandId, categoryId, attributeIds = new[] { attributeId } }));
        return new Catalog(tenant, brandId, categoryId, attributeId, ValueId("Black"), ValueId("Navy"), productId);
    }

    private static Task<HttpResponseMessage> VariantAsync(Catalog c, int valueId, string? barcode = null, decimal price = 50m) =>
        c.Tenant.Owner.PostAsJsonAsync("/api/product-variants", new
        {
            productId = c.ProductId, attributes = new[] { new { attributeId = c.AttributeId, valueId } },
            price, netPrice = 20m, barcode = barcode ?? $"8{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}",
        });

    private static async Task<string> SkuAsync(Catalog c, int variantId)
    {
        var rows = await c.Tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/products/{c.ProductId}/variants?page=1");
        var list = rows.ValueKind == JsonValueKind.Array ? rows : rows.EnumerateObject().First(p => p.Value.ValueKind == JsonValueKind.Array).Value;
        return list.EnumerateArray().First(v => v.GetProperty("id").GetInt32() == variantId).GetProperty("sku").GetString()!;
    }

    [Fact]
    public async Task Sku_is_built_from_names_and_follows_renames()
    {
        var c = await CatalogAsync();
        var variantId = await TestTenant.IdOf(await VariantAsync(c, c.BlackId));
        Assert.Equal("ACME-FIELD-COAT-COLOR-BLACK", await SkuAsync(c, variantId));

        (await c.Tenant.Owner.PutAsJsonAsync($"/api/brands/{c.BrandId}", new { name = "Acme Wear" })).EnsureSuccessStatusCode();
        (await c.Tenant.Owner.PutAsJsonAsync($"/api/products/{c.ProductId}", new { name = "Trench", brandId = c.BrandId, categoryId = c.CategoryId, attributeIds = new[] { c.AttributeId } })).EnsureSuccessStatusCode();
        (await c.Tenant.Owner.PutAsJsonAsync($"/api/attributes/{c.AttributeId}/values/{c.BlackId}", new { value = "Jet Black" })).EnsureSuccessStatusCode();

        Assert.Equal("ACME-WEAR-TRENCH-COLOR-JET-BLACK", await SkuAsync(c, variantId));
        var product = await c.Tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/products/{c.ProductId}");
        Assert.Equal("Trench", product.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Duplicate_variant_or_barcode_is_refused()
    {
        var c = await CatalogAsync();
        await TestTenant.IdOf(await VariantAsync(c, c.BlackId, barcode: "5000000000001"));

        var sameCombination = await VariantAsync(c, c.BlackId);
        var sameBarcode = await VariantAsync(c, c.NavyId, barcode: "5000000000001");

        Assert.Equal(HttpStatusCode.Conflict, sameCombination.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, sameBarcode.StatusCode);
    }

    [Fact]
    public async Task Variant_price_can_be_changed_and_is_used_at_the_till()
    {
        var c = await CatalogAsync();
        var variantId = await TestTenant.IdOf(await VariantAsync(c, c.BlackId, barcode: "5000000000002", price: 50m));

        var patch = await c.Tenant.Owner.PatchAsJsonAsync($"/api/product-variants/{variantId}", new { price = 65.5m });
        Assert.True(patch.IsSuccessStatusCode);
        var lot = await TestTenant.IdOf(await c.Tenant.Owner.PostAsJsonAsync("/api/inventory-lots", new { variantId, lotNumber = "L1" }));
        (await c.Tenant.Owner.PostAsJsonAsync("/api/inventory-adjustments", new { variantId, branchId = c.Tenant.BranchId, inventoryLotId = lot, quantity = 2, reason = "count" })).EnsureSuccessStatusCode();

        var scan = await c.Tenant.Owner.GetFromJsonAsync<JsonElement>("/api/pos/items?code=5000000000002");
        Assert.Equal(65.5m, scan.GetProperty("price").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Tenant.Owner.PatchAsJsonAsync($"/api/product-variants/{variantId}", new { price = -1m })).StatusCode);
    }

    [Fact]
    public async Task Things_in_use_cannot_be_deleted()
    {
        var c = await CatalogAsync();
        await TestTenant.IdOf(await VariantAsync(c, c.BlackId));
        var o = c.Tenant.Owner;

        Assert.Equal(HttpStatusCode.Conflict, (await o.DeleteAsync($"/api/brands/{c.BrandId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await o.DeleteAsync($"/api/categories/{c.CategoryId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await o.DeleteAsync($"/api/attributes/{c.AttributeId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await o.DeleteAsync($"/api/attributes/{c.AttributeId}/values/{c.BlackId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await o.DeleteAsync($"/api/products/{c.ProductId}")).StatusCode);

        // Unused things can go.
        Assert.True((await o.DeleteAsync($"/api/attributes/{c.AttributeId}/values/{c.NavyId}")).IsSuccessStatusCode);
        var spareBrand = await TestTenant.IdOf(await o.PostAsJsonAsync("/api/brands", new { name = "Spare" }));
        Assert.True((await o.DeleteAsync($"/api/brands/{spareBrand}")).IsSuccessStatusCode);
    }

    /// <summary>A variant that was sold keeps its history: deleting it must not break or erase past orders.</summary>
    [Fact]
    public async Task Sold_variant_cannot_be_deleted_even_when_out_of_stock()
    {
        var tenant = await _api.NewTenantAsync("History Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 10m, stock: 1);
        var (orderId, _) = await tenant.OrderAsync((variantId, 1)); // sells the last unit

        var delete = await tenant.Owner.DeleteAsync($"/api/product-variants/{variantId}");

        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        var order = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        Assert.Equal(1, order.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Unsold_empty_variant_can_be_deleted()
    {
        var c = await CatalogAsync();
        var variantId = await TestTenant.IdOf(await VariantAsync(c, c.NavyId));

        Assert.True((await c.Tenant.Owner.DeleteAsync($"/api/product-variants/{variantId}")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Catalogue_reads_answer()
    {
        var c = await CatalogAsync();
        await TestTenant.IdOf(await VariantAsync(c, c.BlackId));
        var o = c.Tenant.Owner;

        foreach (var path in new[] { "/api/products?page=1", "/api/products/catalog", $"/api/products/{c.ProductId}", $"/api/products/{c.ProductId}/variants?page=1",
                     "/api/brands", "/api/categories", "/api/attributes", $"/api/attributes/{c.AttributeId}", "/api/product-variants?page=1&search=coat" })
            Assert.True((await o.GetAsync(path)).IsSuccessStatusCode, path);
    }

    // ---------------------------------------------------------------- promotions

    private static async Task<HttpResponseMessage> SaleAsync(TestTenant tenant, string typeCode, decimal value, int days = 7) =>
        await tenant.Owner.PostAsJsonAsync("/api/sales", new
        {
            name = $"Promo {typeCode} {value}", saleTypeId = await tenant.LookupIdAsync("saleTypes", typeCode), value,
            startDate = DateTime.UtcNow.AddDays(-1), endDate = DateTime.UtcNow.AddDays(days),
        });

    [Fact]
    public async Task Promotion_applies_at_the_till_until_deleted()
    {
        var tenant = await _api.NewTenantAsync("Promo Co");
        var (variantId, _, barcode) = await tenant.VariantWithStockAsync(price: 80m, stock: 3);
        var saleId = await TestTenant.IdOf(await SaleAsync(tenant, "PERCENTAGE", 25m));
        (await tenant.Owner.PatchAsJsonAsync($"/api/product-variants/{variantId}", new { saleId })).EnsureSuccessStatusCode();

        Assert.Equal(20m, (await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/pos/items?code={barcode}")).GetProperty("saleDiscount").GetDecimal());

        (await tenant.Owner.PutAsJsonAsync($"/api/sales/{saleId}", new
        {
            name = "Deeper", saleTypeId = await tenant.LookupIdAsync("saleTypes", "PERCENTAGE"), value = 50m,
            startDate = DateTime.UtcNow.AddDays(-1), endDate = DateTime.UtcNow.AddDays(7),
        })).EnsureSuccessStatusCode();
        Assert.Equal(40m, (await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/pos/items?code={barcode}")).GetProperty("saleDiscount").GetDecimal());

        Assert.True((await tenant.Owner.DeleteAsync($"/api/sales/{saleId}")).IsSuccessStatusCode);
        Assert.Equal(0m, (await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/pos/items?code={barcode}")).GetProperty("saleDiscount").GetDecimal());

        foreach (var path in new[] { "/api/sales?page=1", "/api/sales/variants?q=item" })
            Assert.True((await tenant.Owner.GetAsync(path)).IsSuccessStatusCode, path);
    }

    /// <summary>Found in Phase 7: no screen could put items on a promotion, so promotions never applied.</summary>
    [Fact]
    public async Task Items_are_added_to_and_removed_from_a_promotion()
    {
        var tenant = await _api.NewTenantAsync("Promo Items Co");
        var (a, _, barcodeA) = await tenant.VariantWithStockAsync(price: 50m, stock: 3);
        var (b, _, barcodeB) = await tenant.VariantWithStockAsync(price: 20m, stock: 3);
        var saleId = await TestTenant.IdOf(await SaleAsync(tenant, "PERCENTAGE", 10m));

        var add = await tenant.Owner.PostAsJsonAsync($"/api/sales/{saleId}/variants", new { variantIds = new[] { a, b } });
        Assert.True(add.IsSuccessStatusCode, await add.Content.ReadAsStringAsync());
        Assert.Equal(5m, (await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/pos/items?code={barcodeA}")).GetProperty("saleDiscount").GetDecimal());
        var detail = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/sales/{saleId}?page=1");
        Assert.Equal(2, detail.EnumerateObject().First(p => p.Value.ValueKind == JsonValueKind.Array).Value.GetArrayLength());

        Assert.True((await tenant.Owner.DeleteAsync($"/api/sales/{saleId}/variants/{b}")).IsSuccessStatusCode);
        Assert.Equal(0m, (await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/pos/items?code={barcodeB}")).GetProperty("saleDiscount").GetDecimal());
        Assert.Equal(5m, (await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/pos/items?code={barcodeA}")).GetProperty("saleDiscount").GetDecimal());
    }

    [Fact]
    public async Task Promotion_items_need_the_permission_and_a_live_promotion_of_this_company()
    {
        var tenant = await _api.NewTenantAsync("Promo Guard Co");
        var other = await _api.NewTenantAsync("Other Promo Co");
        var (mine, _, _) = await tenant.VariantWithStockAsync(price: 50m, stock: 1);
        var (theirs, _, _) = await other.VariantWithStockAsync(price: 50m, stock: 1);
        var saleId = await TestTenant.IdOf(await SaleAsync(tenant, "PERCENTAGE", 10m));
        var viewer = await tenant.UserAsync("OrderManager", "sale.view", "variant.view");

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/sales/{saleId}/variants", new { variantIds = new[] { mine } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await tenant.Owner.PostAsJsonAsync($"/api/sales/{saleId}/variants", new { variantIds = new[] { theirs } })).StatusCode);

        (await tenant.Owner.DeleteAsync($"/api/sales/{saleId}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await tenant.Owner.PostAsJsonAsync($"/api/sales/{saleId}/variants", new { variantIds = new[] { mine } })).StatusCode);
    }

    [Fact]
    public async Task Promotion_values_must_make_sense()
    {
        var tenant = await _api.NewTenantAsync("Bad Promo Co");

        Assert.Equal(HttpStatusCode.BadRequest, (await SaleAsync(tenant, "PERCENTAGE", 150m)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaleAsync(tenant, "PERCENTAGE", 0m)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaleAsync(tenant, "FIXED_AMOUNT", -5m)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaleAsync(tenant, "PERCENTAGE", 10m, days: -3)).StatusCode); // ends before it starts
    }
}
