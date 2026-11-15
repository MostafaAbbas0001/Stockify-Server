using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Stockify.Tests;

/// <summary>Customers, employees, users, branches and delivery charges: CRUD and their guards (D24).</summary>
[Collection(nameof(ApiCollection))]
public class PeopleSettingsTests
{
    private readonly ApiFixture _api;

    public PeopleSettingsTests(ApiFixture api) => _api = api;

    private static JsonElement Rows(JsonElement body, string key) =>
        body.ValueKind == JsonValueKind.Array ? body : body.GetProperty(key);

    // ---------------------------------------------------------------- customers

    [Fact]
    public async Task Customers_can_be_created_found_edited_and_deleted()
    {
        var tenant = await _api.NewTenantAsync("Customer Co");
        var o = tenant.Owner;
        var id = await TestTenant.IdOf(await o.PostAsJsonAsync("/api/customers", new { name = "Rana Khalil", phone = "+961 3 111 222", address = "Hamra", email = "rana@example.test" }));

        var found = await o.GetFromJsonAsync<JsonElement>("/api/customers?page=1&search=rana");
        Assert.Contains(Rows(found, "customers").EnumerateArray(), c => c.GetProperty("id").GetInt32() == id);

        (await o.PutAsJsonAsync($"/api/customers/{id}", new { name = "Rana K.", phone = "+961 3 111 223", address = "Verdun", email = "rana@example.test" })).EnsureSuccessStatusCode();
        Assert.Equal("Rana K.", (await o.GetFromJsonAsync<JsonElement>($"/api/customers/{id}")).GetProperty("name").GetString());

        Assert.True((await o.DeleteAsync($"/api/customers/{id}")).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await o.GetAsync($"/api/customers/{id}")).StatusCode);
    }

    [Fact]
    public async Task Customer_with_orders_cannot_be_deleted()
    {
        var tenant = await _api.NewTenantAsync("Loyal Customer Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 10m, stock: 2);
        await tenant.OrderAsync((variantId, 1)); // the walk-in customer now has an order

        var delete = await tenant.Owner.DeleteAsync($"/api/customers/{await tenant.WalkInCustomerIdAsync()}");

        Assert.False(delete.IsSuccessStatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, delete.StatusCode);
    }

    // ---------------------------------------------------------------- employees

    [Fact]
    public async Task Employees_can_be_managed_and_report_their_sales()
    {
        var tenant = await _api.NewTenantAsync("Staff Co", taxRate: 0.11m);
        var o = tenant.Owner;
        var id = await TestTenant.IdOf(await o.PostAsJsonAsync("/api/employees", new { name = "Omar", branchId = tenant.BranchId, isSale = true, salary = 900m, email = "omar@example.test" }));

        (await o.PutAsJsonAsync($"/api/employees/{id}", new { name = "Omar H.", salary = 950m })).EnsureSuccessStatusCode();
        var employee = await o.GetFromJsonAsync<JsonElement>($"/api/employees/{id}");
        Assert.Equal("Omar H.", employee.GetProperty("name").GetString());
        Assert.True((await o.GetAsync("/api/employees?page=1")).IsSuccessStatusCode);
        Assert.True((await o.GetAsync("/api/employees/current-branch")).IsSuccessStatusCode);

        var spare = await TestTenant.IdOf(await o.PostAsJsonAsync("/api/employees", new { name = "Temp", branchId = tenant.BranchId, isSale = false, email = "temp@example.test" }));
        Assert.True((await o.DeleteAsync($"/api/employees/{spare}")).IsSuccessStatusCode);

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var summary = await o.GetFromJsonAsync<JsonElement>($"/api/employees/{id}/sales-summary?startDate={today}&endDate={today}");
        Assert.Equal(0m, summary.EnumerateObject().First(p => p.Value.ValueKind == JsonValueKind.Number && p.Name != "employeeId").Value.GetDecimal());
    }

    [Fact]
    public async Task Employee_with_sales_cannot_be_deleted()
    {
        var tenant = await _api.NewTenantAsync("Seller Co");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 10m, stock: 2);
        await tenant.OrderAsync((variantId, 1));
        var orders = await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/orders?page=1");
        var orderId = orders.GetProperty("orders").EnumerateArray().First().GetProperty("id").GetInt32();
        var detail = await tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        var employees = Rows(await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/employees?page=1"), "employees");
        var seller = employees.EnumerateArray().First(e => e.GetProperty("name").GetString() == detail.GetProperty("createdByEmployeeName").GetString()).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.Conflict, (await tenant.Owner.DeleteAsync($"/api/employees/{seller}")).StatusCode);
    }

    // ---------------------------------------------------------------- users

    private static async Task<int> OwnerUserIdAsync(TestTenant tenant)
    {
        var users = Rows(await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/users?page=1"), "users");
        return users.EnumerateArray().First(u => u.GetProperty("isOwner").GetBoolean()).GetProperty("id").GetInt32();
    }

    private static async Task<int> RoleIdAsync(TestTenant tenant, string name) =>
        (await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/users/roles")).EnumerateArray()
            .First(r => r.GetProperty("name").GetString() == name).GetProperty("id").GetInt32();

    [Fact]
    public async Task Users_can_be_edited_and_deleted_but_owner_and_self_are_protected()
    {
        var tenant = await _api.NewTenantAsync("Users Co");
        var o = tenant.Owner;
        var cashierRole = await RoleIdAsync(tenant, "PointOfSales");
        var managerRole = await RoleIdAsync(tenant, "OrderManager");
        var email = $"u-{Guid.NewGuid():N}@test.local";
        var userId = await TestTenant.IdOf(await o.PostAsJsonAsync("/api/users", new { email, username = "Nour", password = "Secret123!", roleId = cashierRole, branchId = tenant.BranchId }));

        (await o.PutAsJsonAsync($"/api/users/{userId}", new { email, username = "Nour B.", roleId = managerRole, branchId = tenant.BranchId })).EnsureSuccessStatusCode();
        var user = await o.GetFromJsonAsync<JsonElement>($"/api/users/{userId}");
        Assert.Equal("Nour B.", user.GetProperty("username").GetString());

        var ownerId = await OwnerUserIdAsync(tenant);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.DeleteAsync($"/api/users/{ownerId}")).StatusCode); // yourself and the owner
        Assert.Equal(HttpStatusCode.BadRequest, (await o.PutAsJsonAsync($"/api/users/{ownerId}", new { email = tenant.OwnerEmail, username = "Owner", roleId = cashierRole, branchId = tenant.BranchId })).StatusCode);

        var duplicate = await o.PostAsJsonAsync("/api/users", new { email, username = "Copy", password = "Secret123!", roleId = cashierRole, branchId = tenant.BranchId });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        Assert.True((await o.DeleteAsync($"/api/users/{userId}")).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await o.GetAsync($"/api/users/{userId}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_user_who_has_worked_keeps_their_records()
    {
        var tenant = await _api.NewTenantAsync("Leaver Co");
        var mall = await tenant.BranchAsync("Mall");
        var (variantId, _, _) = await tenant.VariantWithStockAsync(price: 10m, stock: 2);
        var email = $"u-{Guid.NewGuid():N}@test.local";
        var userId = await TestTenant.IdOf(await tenant.Owner.PostAsJsonAsync("/api/users", new { email, username = "Leaver", password = ApiFixture.Password, roleId = await RoleIdAsync(tenant, "ExecutiveAdmin"), branchId = tenant.BranchId }));
        var leaver = await tenant.UserAsLoginAsync(email);
        var transferId = await TestTenant.IdOf(await leaver.PostAsJsonAsync("/api/stock-transfers", new
        {
            sourceBranchId = tenant.BranchId, destinationBranchId = mall, items = new[] { new { productVariantId = variantId, quantity = 1 } },
        }));

        var delete = await tenant.Owner.DeleteAsync($"/api/users/{userId}");

        Assert.True(delete.StatusCode != HttpStatusCode.InternalServerError, await delete.Content.ReadAsStringAsync());
        Assert.True((await tenant.Owner.GetAsync($"/api/stock-transfers/{transferId}")).IsSuccessStatusCode);
    }

    // ---------------------------------------------------------------- branches (D24)

    [Fact]
    public async Task Branch_can_be_renamed_and_an_empty_one_deleted()
    {
        var tenant = await _api.NewTenantAsync("Branch Co");
        var spare = await tenant.BranchAsync("Airport");

        (await tenant.Owner.PutAsJsonAsync($"/api/branches/{spare}", new { name = "Airport T2" })).EnsureSuccessStatusCode();
        var names = Rows(await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/branches"), "branches").EnumerateArray().Select(b => b.GetProperty("name").GetString()).ToList();
        Assert.Contains("Airport T2", names);

        Assert.True((await tenant.Owner.DeleteAsync($"/api/branches/{spare}")).IsSuccessStatusCode);
        names = Rows(await tenant.Owner.GetFromJsonAsync<JsonElement>("/api/branches"), "branches").EnumerateArray().Select(b => b.GetProperty("name").GetString()).ToList();
        Assert.DoesNotContain("Airport T2", names);
    }

    [Fact]
    public async Task Branch_with_stock_users_or_last_one_cannot_be_deleted()
    {
        var tenant = await _api.NewTenantAsync("Busy Branch Co");
        var o = tenant.Owner;
        var withStock = await tenant.BranchAsync("Stocked");
        var (variantId, lotId, _) = await tenant.VariantWithStockAsync(price: 10m, stock: 0);
        (await o.PostAsJsonAsync("/api/inventory-adjustments", new { variantId, branchId = withStock, inventoryLotId = lotId, quantity = 3, reason = "count" })).EnsureSuccessStatusCode();
        var withUser = await tenant.BranchAsync("Staffed");
        await tenant.UserAsync("PointOfSales", new[] { "pos.access" }, withUser);

        Assert.Equal(HttpStatusCode.Conflict, (await o.DeleteAsync($"/api/branches/{withStock}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await o.DeleteAsync($"/api/branches/{withUser}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await o.DeleteAsync($"/api/branches/{tenant.BranchId}")).StatusCode); // the owner works there
    }

    [Fact]
    public async Task Users_cannot_be_assigned_to_a_deleted_branch()
    {
        var tenant = await _api.NewTenantAsync("Closed Branch Co");
        var closed = await tenant.BranchAsync("Closed");
        (await tenant.Owner.DeleteAsync($"/api/branches/{closed}")).EnsureSuccessStatusCode();

        var create = await tenant.Owner.PostAsJsonAsync("/api/users", new
        {
            email = $"u-{Guid.NewGuid():N}@test.local", username = "Late", password = "Secret123!",
            roleId = await RoleIdAsync(tenant, "PointOfSales"), branchId = closed,
        });

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
    }

    // ---------------------------------------------------------------- delivery charges

    [Fact]
    public async Task Delivery_charges_can_be_managed_but_used_or_built_in_ones_stay()
    {
        var tenant = await _api.NewTenantAsync("Delivery Co");
        var o = tenant.Owner;
        var id = await TestTenant.IdOf(await o.PostAsJsonAsync("/api/delivery-charges", new { locationName = "Achrafieh", fee = 3m }));
        (await o.PutAsJsonAsync($"/api/delivery-charges/{id}", new { locationName = "Achrafieh", fee = 4m })).EnsureSuccessStatusCode();
        var list = Rows(await o.GetFromJsonAsync<JsonElement>("/api/delivery-charges"), "items");
        Assert.Equal(4m, list.EnumerateArray().First(l => l.GetProperty("id").GetInt32() == id).GetProperty("fee").GetDecimal());

        Assert.Equal(HttpStatusCode.Conflict, (await o.PostAsJsonAsync("/api/delivery-charges", new { locationName = "achrafieh", fee = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.DeleteAsync($"/api/delivery-charges/{await tenant.InStoreLocationIdAsync()}")).StatusCode);
        Assert.True((await o.DeleteAsync($"/api/delivery-charges/{id}")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Settings_reads_answer()
    {
        var tenant = await _api.NewTenantAsync("Settings Co");
        foreach (var path in new[] { "/api/currencies", "/api/tax-rates", "/api/lookups", "/api/orders/statuses", "/api/invoices/statuses", "/api/stock-movements/types", "/api/stock-transfers/statistics", "/api/inventory-adjustments?page=1" })
            Assert.True((await tenant.Owner.GetAsync(path)).IsSuccessStatusCode, path);
    }
}
