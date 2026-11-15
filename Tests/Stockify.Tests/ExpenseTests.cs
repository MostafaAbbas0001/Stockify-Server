using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Stockify.Tests;

/// <summary>Expenses: line types, commission on sales, payment requests and their approval (D23).</summary>
[Collection(nameof(ApiCollection))]
public class ExpenseTests
{
    private readonly ApiFixture _api;

    public ExpenseTests(ApiFixture api) => _api = api;

    private static async Task<(int parentId, int childId)> CategoriesAsync(TestTenant tenant)
    {
        var parents = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/expenses/categories");
        var employee = parents.EnumerateArray().First(c => c.GetProperty("code").GetString() == "1000").GetProperty("id").GetInt32();
        var children = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/expenses/categories?parentId={employee}");
        return (employee, children.EnumerateArray().First().GetProperty("id").GetInt32());
    }

    private static async Task<HttpResponseMessage> CreateAsync(TestTenant tenant, object line, DateTime? date = null)
    {
        var (parentId, childId) = await CategoriesAsync(tenant);
        var lineJson = JsonSerializer.SerializeToElement(line);
        var merged = lineJson.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        merged["categoryId"] = childId;
        return await tenant.Owner.PostAsJsonAsync("/api/expenses", new
        {
            expenseDate = date ?? DateTime.UtcNow, expenseCategoryId = parentId, lines = new[] { merged },
        });
    }

    private static async Task<JsonElement> LineAsync(TestTenant tenant, int expenseId) =>
        (await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/expenses/{expenseId}")).GetProperty("lines").EnumerateArray().Single();

    private static async Task<int> TypeAsync(TestTenant tenant, string code) => await tenant.LookupIdAsync("expenseLineTypes", code);

    private static async Task SellAsync(TestTenant tenant, int employeeId, int variantId, int quantity)
    {
        var response = await tenant.Owner.PostAsJsonAsync("/api/pos/checkout", new
        {
            idempotencyKey = Guid.NewGuid().ToString(),
            customerId = await tenant.WalkInCustomerIdAsync(),
            deliveryLocationId = await tenant.InStoreLocationIdAsync(),
            createdByEmployeeId = employeeId,
            discountTypeId = (int?)null,
            discountValue = 0,
            items = new[] { new { productVariantId = variantId, quantity } },
            payment = new
            {
                paymentMethodId = await tenant.LookupIdAsync("paymentMethods", "CASH"),
                currencyId = await tenant.BaseCurrencyIdAsync(),
                tendered = 10_000m,
                reference = "",
            },
        });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Amount_fixed_and_quantity_lines_compute_their_totals()
    {
        var tenant = await _api.NewTenantAsync("Expense Co");

        var amount = await TestTenant.IdOf(await CreateAsync(tenant, new { amount = 250.50m, lineTypeId = await TypeAsync(tenant, "AMOUNT") }));
        var @fixed = await TestTenant.IdOf(await CreateAsync(tenant, new { amount = 0m, rate = 1200m, lineTypeId = await TypeAsync(tenant, "FIXED") }));
        var quantity = await TestTenant.IdOf(await CreateAsync(tenant, new { amount = 0m, quantity = 3, unitCost = 19.99m, lineTypeId = await TypeAsync(tenant, "QUANTITY") }));

        Assert.Equal(250.50m, (await LineAsync(tenant, amount)).GetProperty("totalAmount").GetDecimal());
        Assert.Equal(1200m, (await LineAsync(tenant, @fixed)).GetProperty("totalAmount").GetDecimal());
        Assert.Equal(59.97m, (await LineAsync(tenant, quantity)).GetProperty("totalAmount").GetDecimal());
        Assert.Equal("DRAFT", (await LineAsync(tenant, amount)).GetProperty("statusCode").GetString());
    }

    /// <summary>
    /// Commission is a percentage of what the employee actually sold: net of tax and delivery, after
    /// promotions and discounts, less refunds, over whole days of the period (D23).
    /// </summary>
    [Fact]
    public async Task Percentage_line_is_a_share_of_the_employees_net_sales()
    {
        var tenant = await _api.NewTenantAsync("Commission Co", taxRate: 0.11m);
        var employeeId = await tenant.SalespersonAsync();
        var otherEmployee = await tenant.SalespersonAsync();
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 100m, stock: 10);

        await SellAsync(tenant, employeeId, variantId, 1);   // net 100 (total 111 with tax)
        await SellAsync(tenant, employeeId, variantId, 2);   // net 200, then one unit returned → 100
        await SellAsync(tenant, otherEmployee, variantId, 1); // someone else's sale

        var orders = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/invoices?page=1");
        var twoUnitInvoice = orders.GetProperty("invoices").EnumerateArray().First(i => i.GetProperty("totalAmount").GetDecimal() == 222m).GetProperty("id").GetInt32();
        var invoice = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/invoices/{twoUnitInvoice}");
        var lineId = invoice.GetProperty("invoiceItems").EnumerateArray().Single().GetProperty("id").GetInt32();
        (await tenant.Owner.PostAsJsonAsync($"/api/invoices/{twoUnitInvoice}/returns", new
        {
            reason = "one back", items = new[] { new { invoiceItemId = lineId, quantity = 1, reason = "x" } },
        })).EnsureSuccessStatusCode();

        var today = DateTime.UtcNow.Date;
        var expenseId = await TestTenant.IdOf(await CreateAsync(tenant, new
        {
            amount = 0m, rate = 10m, employeeId, startDate = today, endDate = today, // a one-day period: today
            lineTypeId = await TypeAsync(tenant, "PERCENTAGE"),
        }));

        Assert.Equal(20.00m, (await LineAsync(tenant, expenseId)).GetProperty("totalAmount").GetDecimal()); // 10% of (100 + 100)
    }

    [Fact]
    public async Task Line_types_require_their_inputs()
    {
        var tenant = await _api.NewTenantAsync("Strict Lines Co");

        var percentageWithoutEmployee = await CreateAsync(tenant, new { amount = 500m, rate = 10m, lineTypeId = await TypeAsync(tenant, "PERCENTAGE") });
        var quantityWithoutUnitCost = await CreateAsync(tenant, new { amount = 500m, quantity = 2, lineTypeId = await TypeAsync(tenant, "QUANTITY") });
        var negative = await CreateAsync(tenant, new { amount = -5m, lineTypeId = await TypeAsync(tenant, "AMOUNT") });

        Assert.Equal(HttpStatusCode.BadRequest, percentageWithoutEmployee.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, quantityWithoutUnitCost.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
    }

    private static async Task<HttpResponseMessage> RequestPaymentAsync(TestTenant tenant, int lineId, decimal amount) =>
        await tenant.Owner.PostAsJsonAsync("/api/expenses/lines/payments", new
        {
            expenseLineId = lineId, amount, paymentDate = DateTime.UtcNow,
            paymentMethodId = await tenant.LookupIdAsync("paymentMethods", "CASH"),
        });

    private static Task<HttpResponseMessage> DecideAsync(HttpClient client, int paymentId, bool approve, string? reason = null) =>
        client.PostAsJsonAsync($"/api/expenses/lines/payments/{paymentId}/decision", new { isApproved = approve, rejectionReason = reason });

    [Fact]
    public async Task Payments_are_requested_then_approved_or_rejected()
    {
        var tenant = await _api.NewTenantAsync("Payables Co");
        var expenseId = await TestTenant.IdOf(await CreateAsync(tenant, new { amount = 100m, lineTypeId = await TypeAsync(tenant, "AMOUNT") }));
        var lineId = (await LineAsync(tenant, expenseId)).GetProperty("id").GetInt32();

        var first = await TestTenant.IdOf(await RequestPaymentAsync(tenant, lineId, 40m));
        Assert.Equal("DRAFT", (await LineAsync(tenant, expenseId)).GetProperty("statusCode").GetString()); // pending does not count
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(tenant.Owner, first, approve: true)).StatusCode);
        Assert.Equal("PARTIALLY_PAID", (await LineAsync(tenant, expenseId)).GetProperty("statusCode").GetString());

        var rejected = await TestTenant.IdOf(await RequestPaymentAsync(tenant, lineId, 60m));
        Assert.Equal(HttpStatusCode.BadRequest, (await DecideAsync(tenant.Owner, rejected, approve: false)).StatusCode); // reason required
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(tenant.Owner, rejected, approve: false, "wrong invoice")).StatusCode);
        Assert.Equal("PARTIALLY_PAID", (await LineAsync(tenant, expenseId)).GetProperty("statusCode").GetString());

        var tooMuch = await TestTenant.IdOf(await RequestPaymentAsync(tenant, lineId, 61m));
        Assert.Equal(HttpStatusCode.BadRequest, (await DecideAsync(tenant.Owner, tooMuch, approve: true)).StatusCode);
        var pendingDelete = await tenant.Owner.DeleteAsync($"/api/expenses/lines/payments/{tooMuch}");
        Assert.Equal(HttpStatusCode.NoContent, pendingDelete.StatusCode);

        var rest = await TestTenant.IdOf(await RequestPaymentAsync(tenant, lineId, 60m));
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(tenant.Owner, rest, approve: true)).StatusCode);
        Assert.Equal("FULLY_PAID", (await LineAsync(tenant, expenseId)).GetProperty("statusCode").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await tenant.Owner.DeleteAsync($"/api/expenses/lines/payments/{rest}")).StatusCode); // decided
    }

    [Fact]
    public async Task A_payment_decision_is_final()
    {
        var tenant = await _api.NewTenantAsync("Final Decision Co");
        var expenseId = await TestTenant.IdOf(await CreateAsync(tenant, new { amount = 100m, lineTypeId = await TypeAsync(tenant, "AMOUNT") }));
        var lineId = (await LineAsync(tenant, expenseId)).GetProperty("id").GetInt32();
        var approved = await TestTenant.IdOf(await RequestPaymentAsync(tenant, lineId, 100m));
        (await DecideAsync(tenant.Owner, approved, approve: true)).EnsureSuccessStatusCode();

        var flip = await DecideAsync(tenant.Owner, approved, approve: false, "changed my mind");

        Assert.Equal(HttpStatusCode.Conflict, flip.StatusCode);
        Assert.Equal("FULLY_PAID", (await LineAsync(tenant, expenseId)).GetProperty("statusCode").GetString());
    }

    [Fact]
    public async Task Payment_amount_must_be_at_least_one_cent()
    {
        var tenant = await _api.NewTenantAsync("Cent Expense Co");
        var expenseId = await TestTenant.IdOf(await CreateAsync(tenant, new { amount = 100m, lineTypeId = await TypeAsync(tenant, "AMOUNT") }));
        var lineId = (await LineAsync(tenant, expenseId)).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.BadRequest, (await RequestPaymentAsync(tenant, lineId, -10m)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RequestPaymentAsync(tenant, lineId, 0m)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RequestPaymentAsync(tenant, lineId, 0.001m)).StatusCode);
    }

    [Fact]
    public async Task Approving_two_full_payments_at_once_pays_the_line_once()
    {
        var tenant = await _api.NewTenantAsync("Race Expense Co");
        var expenseId = await TestTenant.IdOf(await CreateAsync(tenant, new { amount = 100m, lineTypeId = await TypeAsync(tenant, "AMOUNT") }));
        var lineId = (await LineAsync(tenant, expenseId)).GetProperty("id").GetInt32();
        var a = await TestTenant.IdOf(await RequestPaymentAsync(tenant, lineId, 100m));
        var b = await TestTenant.IdOf(await RequestPaymentAsync(tenant, lineId, 100m));

        var results = await Task.WhenAll(DecideAsync(tenant.Owner, a, true), DecideAsync(tenant.Owner, b, true));

        Assert.Equal(1, results.Count(r => r.IsSuccessStatusCode));
        var approvedTotal = (await LineAsync(tenant, expenseId)).GetProperty("payments").EnumerateArray()
            .Where(p => p.GetProperty("approvedAt").ValueKind != JsonValueKind.Null && p.GetProperty("rejectionReason").ValueKind == JsonValueKind.Null)
            .Sum(p => p.GetProperty("amount").GetDecimal());
        Assert.Equal(100m, approvedTotal);
    }

    [Fact]
    public async Task Only_approvers_decide_payments()
    {
        var tenant = await _api.NewTenantAsync("Approver Co");
        var expenseId = await TestTenant.IdOf(await CreateAsync(tenant, new { amount = 100m, lineTypeId = await TypeAsync(tenant, "AMOUNT") }));
        var lineId = (await LineAsync(tenant, expenseId)).GetProperty("id").GetInt32();
        var payment = await TestTenant.IdOf(await RequestPaymentAsync(tenant, lineId, 50m));
        var clerk = await tenant.UserAsync("OrderManager", "expense.view", "expense.create");

        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(clerk, payment, approve: true)).StatusCode);
    }
}
