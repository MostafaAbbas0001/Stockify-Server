using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Stockify.Models.Entities;

namespace Stockify.Tests;

[Collection(nameof(ApiCollection))]
public class ReportTests
{
    private readonly ApiFixture _api;

    public ReportTests(ApiFixture api) => _api = api;

    private static async Task<IXLWorksheet> SheetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        return workbook.Worksheets.First();
    }

    private static Dictionary<string, int> Columns(IXLWorksheet sheet) =>
        sheet.Row(1).CellsUsed().ToDictionary(cell => cell.GetString(), cell => cell.Address.ColumnNumber);

    private static async Task PayAsync(TestTenant tenant, int invoiceId, decimal amount, int currencyId)
    {
        var response = await tenant.Owner.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            amount, paymentMethodId = await tenant.LookupIdAsync("paymentMethods", "CASH"), reference = "", currencyId,
        });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Audit F-01, F-02, F-03: LBP added to USD, promotions missing from the columns, void invoices counted.</summary>
    [Theory]
    [InlineData("daily-sales?date={0}")]
    [InlineData("monthly-sales?startDate={0}&endDate={0}")]
    public async Task Sales_sheet_rows_add_up_exclude_void_and_convert_currency(string pathTemplate)
    {
        var tenant = await _api.NewTenantAsync("Report Co", secondCurrencyRate: 89500m);
        var (jacket, _, _) = await tenant.VariantWithStockAsync(price: 129.99m, stock: 10);
        var (shirt, _, _) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 10);
        await tenant.AssignSaleAsync(jacket, await tenant.SaleAsync(SaleTypeCodes.Percentage, 20m));
        var currencies = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/currencies");
        var lbp = currencies.EnumerateArray().First(c => c.GetProperty("code").GetString() == "LBP").GetProperty("id").GetInt32();

        var (_, promoted) = await tenant.OrderAsync((jacket, 1));             // 103.99 net + 11.44 tax = 115.43
        await PayAsync(tenant, promoted, 115.43m, await tenant.BaseCurrencyIdAsync());
        var (_, paidInLbp) = await tenant.OrderAsync((shirt, 1));             // 11.10 = 993 450 LBP
        await PayAsync(tenant, paidInLbp, 993450m, lbp);
        var (cancelled, _) = await tenant.OrderAsync((shirt, 1));
        (await tenant.Owner.PostAsJsonAsync($"/api/orders/{cancelled}/cancel", new { reason = "test" })).EnsureSuccessStatusCode();

        var sheet = await SheetAsync(tenant.Owner, "/api/reports/" + string.Format(pathTemplate, DateTime.UtcNow.ToString("yyyy-MM-dd")));
        var col = Columns(sheet);
        var rows = sheet.RowsUsed().Skip(1).ToList();

        Assert.Equal(2, rows.Count); // the void invoice is not a sale
        foreach (var row in rows)
        {
            decimal Value(string header) => row.Cell(col[header]).GetValue<decimal>();
            Assert.Equal(Value("Total"), Value("Subtotal") - Value("Sale") - Value("Discount") + Value("Tax") + Value("Delivery"));
            Assert.Equal(0m, Value("Remaining Dues"));
        }
    }

    /// <summary>Review I5: branch and staff performance sheets had the same bugs as the sales sheets.</summary>
    [Fact]
    public async Task Branch_performance_rows_add_up_exclude_void_and_convert_currency()
    {
        var tenant = await _api.NewTenantAsync("Branch Perf Co", secondCurrencyRate: 89500m);
        var (jacket, _, _) = await tenant.VariantWithStockAsync(price: 129.99m, stock: 10);
        var (shirt, _, _) = await tenant.VariantWithStockAsync(price: 10.00m, stock: 10);
        await tenant.AssignSaleAsync(jacket, await tenant.SaleAsync(SaleTypeCodes.Percentage, 20m));
        var currencies = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/currencies");
        var lbp = currencies.EnumerateArray().First(c => c.GetProperty("code").GetString() == "LBP").GetProperty("id").GetInt32();
        var (_, promoted) = await tenant.OrderAsync((jacket, 1));
        await PayAsync(tenant, promoted, 115.43m, await tenant.BaseCurrencyIdAsync());
        var (_, paidInLbp) = await tenant.OrderAsync((shirt, 1));
        await PayAsync(tenant, paidInLbp, 993450m, lbp);
        var (cancelled, _) = await tenant.OrderAsync((shirt, 1));
        (await tenant.Owner.PostAsJsonAsync($"/api/orders/{cancelled}/cancel", new { reason = "test" })).EnsureSuccessStatusCode();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var response = await tenant.Owner.GetAsync($"/api/reports/branch-performance?startDate={today}&endDate={today}&branchId={tenant.BranchId}");
        response.EnsureSuccessStatusCode();
        var detail = new XLWorkbook(await response.Content.ReadAsStreamAsync()).Worksheets
            .First(sheet => sheet.Row(1).CellsUsed().Any(cell => cell.GetString() == "Invoice #"));
        var col = Columns(detail);
        var rows = detail.RowsUsed().Skip(1).ToList();

        Assert.Equal(2, rows.Count);
        foreach (var row in rows)
        {
            decimal Value(string header) => row.Cell(col[header]).GetValue<decimal>();
            Assert.Equal(Value("Total"), Value("Subtotal") - Value("Discount") + Value("Tax") + Value("Delivery"));
            Assert.Equal(Value("Total"), Value("Paid"));
        }
    }

    /// <summary>Audit F-08: stock on hand counted units in expired lots as sellable.</summary>
    [Fact]
    public async Task Stock_on_hand_excludes_expired_lots()
    {
        var tenant = await _api.NewTenantAsync("Expiry Co");
        var (variantId, lotId, _) = await tenant.VariantWithStockAsync(price: 2.00m, stock: 7, name: "Yogurt");
        await _api.ExecuteSqlAsync("UPDATE \"InventoryLots\" SET \"ExpiryDate\" = CURRENT_DATE - 1 WHERE \"Id\" = @id", ("id", lotId));

        var sheet = await SheetAsync(tenant.Owner, "/api/reports/stock-on-hand");

        Assert.DoesNotContain(sheet.RowsUsed().Skip(1), row => row.Cell(Columns(sheet)["Product"]).GetString() == "Yogurt");
        _ = variantId;
    }
}
