using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Stockify.Tests;

[Collection(nameof(ApiCollection))]
public class ListFilterTests
{
    private readonly ApiFixture _api;

    public ListFilterTests(ApiFixture api) => _api = api;

    /// <summary>Audit G-01: plain dates bound as DateTimeKind.Unspecified and Npgsql rejected them (400).</summary>
    [Theory]
    [InlineData("orders", "orders")]
    [InlineData("invoices", "invoices")]
    [InlineData("stock-transfers", null)]
    [InlineData("inventory-adjustments", "adjustments")]
    [InlineData("stock-movements", null)]
    [InlineData("sales", null)]
    public async Task Date_filters_accept_plain_dates(string resource, string? listKey)
    {
        var tenant = await _api.NewTenantAsync("Dates Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 5);
        await tenant.OrderAsync((variantId, 1));
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var weekAgo = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd");

        var response = await tenant.Owner.GetAsync($"/api/{resource}?page=1&startDate={weekAgo}&endDate={today}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (listKey != null)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.GetProperty(listKey).GetArrayLength() >= 1, $"today's {resource} row missing from the range");
        }
    }
}
