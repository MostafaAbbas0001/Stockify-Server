using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;

namespace Stockify.Tests;

/// <summary>The reports not covered by ReportTests, dashboard charts, platform admin and session tokens.</summary>
[Collection(nameof(ApiCollection))]
public class ReportsPlatformAuthTests
{
    private readonly ApiFixture _api;

    public ReportsPlatformAuthTests(ApiFixture api) => _api = api;

    private static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd");

    private static async Task<XLWorkbook> WorkbookAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.True(response.IsSuccessStatusCode, $"{path} -> {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        return new XLWorkbook(await response.Content.ReadAsStreamAsync());
    }

    private static List<Dictionary<string, string>> Rows(IXLWorksheet sheet)
    {
        var header = sheet.Row(1).CellsUsed().ToDictionary(c => c.Address.ColumnNumber, c => c.GetString());
        return sheet.RowsUsed().Skip(1)
            .Select(row => header.ToDictionary(h => h.Value, h => row.Cell(h.Key).GetFormattedString()))
            .Where(row => row.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
            .ToList();
    }

    private static async Task<int> CustomerAsync(TestTenant tenant, string name, string phone) =>
        await TestTenant.IdOf(await tenant.Owner.PostAsJsonAsync("/api/customers", new { name, phone, address = "Beirut", email = $"{Guid.NewGuid():N}@example.test" }));

    private static async Task<(int orderId, int invoiceId)> SellToAsync(TestTenant tenant, int customerId, int variantId, int quantity, bool pay = true, int? deliveryLocationId = null)
    {
        var response = await tenant.Owner.PostAsJsonAsync("/api/orders", new
        {
            customerId, deliveryLocationId = deliveryLocationId ?? await tenant.InStoreLocationIdAsync(),
            createdByEmployeeId = await tenant.SalespersonAsync(), discountTypeId = (int?)null, discountValue = 0,
            items = new[] { new { productVariantId = variantId, quantity } },
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.IsSuccessStatusCode, body.ToString());
        var (orderId, invoiceId) = (body.GetProperty("orderId").GetInt32(), body.GetProperty("invoiceId").GetInt32());
        if (pay)
        {
            var invoice = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/invoices/{invoiceId}");
            (await tenant.Owner.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
            {
                amount = invoice.GetProperty("totalAmount").GetDecimal(), paymentMethodId = await tenant.LookupIdAsync("paymentMethods", "CASH"),
                reference = "", currencyId = await tenant.BaseCurrencyIdAsync(),
            })).EnsureSuccessStatusCode();
        }
        return (orderId, invoiceId);
    }

    // ---------------------------------------------------------------- reports

    /// <summary>
    /// "Total Paid" was the gross subtotal of every order, cancelled and unpaid ones included, and the
    /// customer filter matched by name so two customers called the same were merged.
    /// </summary>
    [Fact]
    public async Task Customers_report_counts_money_actually_paid_per_customer()
    {
        var tenant = await _api.NewTenantAsync("Customer Report Co", taxRate: 0.11m);
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 100m, stock: 20);
        var samA = await CustomerAsync(tenant, "Sam", "+961 1 000 001");
        var samB = await CustomerAsync(tenant, "Sam", "+961 1 000 002");

        await SellToAsync(tenant, samA, variantId, 1);                   // paid 111.00
        await SellToAsync(tenant, samA, variantId, 1, pay: false);       // unpaid
        var (cancelled, _) = await SellToAsync(tenant, samA, variantId, 1, pay: false);
        (await tenant.Owner.PostAsJsonAsync($"/api/orders/{cancelled}/cancel", new { reason = "x" })).EnsureSuccessStatusCode();
        await SellToAsync(tenant, samB, variantId, 2);                   // the other Sam: 222.00

        var book = await WorkbookAsync(tenant.Owner, $"/api/reports/customers?startDate={Today}&customerId={samA}");
        var summary = Rows(book.Worksheet(1));

        var row = Assert.Single(summary);
        Assert.Equal("2", row["Orders"]);                                // cancelled order left out
        Assert.Equal(111m, decimal.Parse(row.First(kv => kv.Key.StartsWith("Total Paid")).Value));
    }

    [Fact]
    public async Task Low_stock_report_lists_items_under_their_reorder_level()
    {
        var tenant = await _api.NewTenantAsync("Low Stock Co");
        var (low, _, _) = await tenant.VariantWithStockAsync(price: 5m, stock: 1, name: "Low Item");
        var (fine, _, _) = await tenant.VariantWithStockAsync(price: 5m, stock: 50, name: "Fine Item");
        foreach (var variantId in new[] { low, fine })
            (await tenant.Owner.PutAsJsonAsync("/api/inventory-adjustments/reorder-level", new { variantId, branchId = tenant.BranchId, reorderLevel = 5 })).EnsureSuccessStatusCode();

        var rows = Rows((await WorkbookAsync(tenant.Owner, "/api/reports/low-stock")).Worksheet(1));

        var row = Assert.Single(rows, r => r["Product"] == "Low Item");
        Assert.Equal("4", row["Deficit"]);
        Assert.DoesNotContain(rows, r => r["Product"] == "Fine Item");
    }

    [Fact]
    public async Task Expenses_report_shows_paid_and_remaining()
    {
        var tenant = await _api.NewTenantAsync("Expense Report Co");
        var parents = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/expenses/categories");
        var parent = parents.EnumerateArray().First(c => c.GetProperty("code").GetString() == "1000").GetProperty("id").GetInt32();
        var child = (await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/expenses/categories?parentId={parent}")).EnumerateArray().First().GetProperty("id").GetInt32();
        var expenseId = await TestTenant.IdOf(await tenant.Owner.PostAsJsonAsync("/api/expenses", new
        {
            expenseDate = DateTime.UtcNow, expenseCategoryId = parent,
            lines = new[] { new { categoryId = child, amount = 100m, lineTypeId = await tenant.LookupIdAsync("expenseLineTypes", "AMOUNT") } },
        }));
        var lineId = (await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/expenses/{expenseId}")).GetProperty("lines").EnumerateArray().Single().GetProperty("id").GetInt32();
        var paymentId = await TestTenant.IdOf(await tenant.Owner.PostAsJsonAsync("/api/expenses/lines/payments", new
        {
            expenseLineId = lineId, amount = 40m, paymentDate = DateTime.UtcNow, paymentMethodId = await tenant.LookupIdAsync("paymentMethods", "CASH"),
        }));
        (await tenant.Owner.PostAsJsonAsync($"/api/expenses/lines/payments/{paymentId}/decision", new { isApproved = true })).EnsureSuccessStatusCode();

        var row = Assert.Single(Rows((await WorkbookAsync(tenant.Owner, $"/api/reports/expenses?from={Today}&to={Today}")).Worksheet(1)));

        Assert.Equal(100m, decimal.Parse(row["Total Amount"]));
        Assert.Equal(40m, decimal.Parse(row["Paid"]));
        Assert.Equal(60m, decimal.Parse(row["Remaining Dues"]));
    }

    [Fact]
    public async Task Deliveries_staff_and_orders_reports_list_the_right_orders()
    {
        var tenant = await _api.NewTenantAsync("Delivery Report Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 20m, stock: 10);
        var home = await TestTenant.IdOf(await tenant.Owner.PostAsJsonAsync("/api/delivery-charges", new { locationName = "Home", fee = 5m }));
        var customer = await tenant.WalkInCustomerIdAsync();
        var (delivered, _) = await SellToAsync(tenant, customer, variantId, 1, deliveryLocationId: home);
        var (inStore, _) = await SellToAsync(tenant, customer, variantId, 1);

        var deliveries = Rows((await WorkbookAsync(tenant.Owner, "/api/reports/deliveries")).Worksheet(1));
        var orderNumbers = deliveries.Select(r => r["Order #"]).ToList();
        var deliveredNumber = (await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/orders/{delivered}")).GetProperty("orderNumber").GetString();
        var inStoreNumber = (await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/orders/{inStore}")).GetProperty("orderNumber").GetString();
        Assert.Contains(deliveredNumber, orderNumbers);
        Assert.DoesNotContain(inStoreNumber, orderNumbers);

        var orders = Rows((await WorkbookAsync(tenant.Owner, $"/api/reports/orders?startDate={Today}")).Worksheet(1));
        Assert.Equal(2, orders.Count);

        var employees = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/employees?page=1");
        var list = employees.ValueKind == JsonValueKind.Array ? employees : employees.GetProperty("employees");
        foreach (var employee in list.EnumerateArray())
            await WorkbookAsync(tenant.Owner, $"/api/reports/staff-performance?startDate={Today}&employeeId={employee.GetProperty("id").GetInt32()}");
    }

    /// <summary>Same rule as backlog #22: a branch-limited manager only exports their own branch.</summary>
    [Fact]
    public async Task Branch_limited_manager_exports_only_their_branch()
    {
        var tenant = await _api.NewTenantAsync("Report Scope Co");
        var mall = await tenant.BranchAsync("Mall");
        var (variantId, lotId, _) = await tenant.VariantWithStockAsync(price: 10m, stock: 5, name: "Main Stock");
        (await tenant.Owner.PostAsJsonAsync("/api/inventory-adjustments", new { variantId, branchId = mall, inventoryLotId = lotId, quantity = 7, reason = "mall" })).EnsureSuccessStatusCode();
        await SellToAsync(tenant, await tenant.WalkInCustomerIdAsync(), variantId, 1); // a main-branch order
        var mallManager = await tenant.UserAsync("OrderManager", new[] { "reports.tab.view", "stock.view", "order.view", "customer.view" }, mall);

        var stock = Rows((await WorkbookAsync(mallManager, "/api/reports/stock-on-hand")).Worksheet(1));
        var orders = Rows((await WorkbookAsync(mallManager, $"/api/reports/orders?startDate={Today}")).Worksheet(1));
        var customers = Rows((await WorkbookAsync(mallManager, $"/api/reports/customers?startDate={Today}")).Worksheet(1));

        Assert.All(stock, row => Assert.Equal("Mall", row["Branch"]));
        Assert.Empty(orders);
        Assert.Empty(customers);
    }

    // ---------------------------------------------------------------- dashboard

    [Fact]
    public async Task Top_products_count_what_was_sold_not_what_was_cancelled()
    {
        var tenant = await _api.NewTenantAsync("Top Co");
        var (sold, _, _) = await tenant.VariantWithStockAsync(price: 10m, stock: 10, name: "Sold Item");
        var (cancelledItem, _, _) = await tenant.VariantWithStockAsync(price: 10m, stock: 10, name: "Cancelled Item");
        await SellToAsync(tenant, await tenant.WalkInCustomerIdAsync(), sold, 3);
        var (cancelled, _) = await SellToAsync(tenant, await tenant.WalkInCustomerIdAsync(), cancelledItem, 5, pay: false);
        (await tenant.Owner.PostAsJsonAsync($"/api/orders/{cancelled}/cancel", new { reason = "x" })).EnsureSuccessStatusCode();

        var top = (await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/dashboard/top-products")).ToString();

        Assert.Contains("Sold Item", top);
        Assert.DoesNotContain("Cancelled Item", top);
        Assert.True((await tenant.Owner.GetAsync("/api/dashboard/stock-demand-trend")).IsSuccessStatusCode);
        Assert.True((await tenant.Owner.GetAsync("/api/dashboard/expenses-summary")).IsSuccessStatusCode);
    }

    // ---------------------------------------------------------------- platform admin

    [Fact]
    public async Task New_company_gets_its_defaults_and_can_be_suspended_and_reactivated()
    {
        var tenant = await _api.NewTenantAsync("Provisioned Co", businessType: "CAFE");
        var o = tenant.Owner;

        Assert.NotEqual(0, await tenant.WalkInCustomerIdAsync());
        Assert.NotEqual(0, await tenant.InStoreLocationIdAsync());
        Assert.NotEqual(0, await tenant.BaseCurrencyIdAsync());
        Assert.True((await o.GetFromJsonAsync<JsonElement>("/api/expenses/categories")).GetArrayLength() > 0);
        Assert.True((await o.GetFromJsonAsync<JsonElement>("/api/tax-rates")).ToString().Length > 2);

        using var platform = _api.As(await _api.PlatformLoginAsync());
        var tenants = (await platform.GetFromJsonAsync<JsonElement>("/api/platform/tenants")).ToString();
        Assert.Contains("Provisioned Co", tenants);

        (await platform.PatchAsJsonAsync($"/api/platform/tenants/{tenant.TenantId}/status", new { status = "SUSPENDED" })).EnsureSuccessStatusCode();
        var blocked = await o.GetAsync("/api/lookups");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("TENANT_SUSPENDED", await blocked.Content.ReadAsStringAsync());
        var login = await _api.Client.PostAsJsonAsync("/api/auth/login", new { email = tenant.OwnerEmail, password = ApiFixture.Password });
        Assert.False(login.IsSuccessStatusCode);

        (await platform.PatchAsJsonAsync($"/api/platform/tenants/{tenant.TenantId}/status", new { status = "ACTIVE" })).EnsureSuccessStatusCode();
        Assert.True((await o.GetAsync("/api/lookups")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Company_users_cannot_use_platform_endpoints()
    {
        var tenant = await _api.NewTenantAsync("Not Platform Co");

        Assert.Equal(HttpStatusCode.Forbidden, (await tenant.Owner.GetAsync("/api/platform/tenants")).StatusCode);
        var wrongPassword = await _api.Client.PostAsJsonAsync("/api/platform/auth/login", new { email = ApiFixture.PlatformEmail, password = "nope-nope-nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
    }

    // ---------------------------------------------------------------- sessions

    [Fact]
    public async Task Refresh_token_rotates_and_logout_ends_the_session()
    {
        var tenant = await _api.NewTenantAsync("Session Co");
        var login = await (await _api.Client.PostAsJsonAsync("/api/auth/login", new { email = tenant.OwnerEmail, password = ApiFixture.Password }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var first = login.GetProperty("refreshToken").GetString();

        var refreshed = await _api.Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first });
        Assert.True(refreshed.IsSuccessStatusCode);
        var second = (await refreshed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString();
        Assert.NotEqual(first, second);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first })).StatusCode); // used once
        (await _api.Client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = second })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = second })).StatusCode);

        var wrong = await _api.Client.PostAsJsonAsync("/api/auth/login", new { email = tenant.OwnerEmail, password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }
}
