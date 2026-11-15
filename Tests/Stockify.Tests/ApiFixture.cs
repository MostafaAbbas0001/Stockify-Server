using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Stockify.Data;

namespace Stockify.Tests;

/// <summary>
/// One API host per test run on its own PostgreSQL database (migrated at start, dropped at the end).
/// Tests talk to the API over HTTP exactly like the client does; <see cref="NewTenantAsync"/> provisions
/// an isolated company through the platform endpoint so tests never depend on each other's data.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string PlatformEmail = "platform@test.local";
    public const string PlatformPassword = "Platform-Test-1234";
    public const string Password = "Test1234!";

    private readonly string _databaseName = $"stockify_test_{Guid.NewGuid():N}";
    private string _adminConnection = null!;
    private string _testConnection = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // The app's .env holds the real connection string; the test database is created next to it with
        // the same credentials. Look for it above the test sources, then above the test binaries.
        var dotEnv = FindDotEnv(SourceDirectory()) ?? FindDotEnv(AppContext.BaseDirectory);
        if (dotEnv is not null)
            DotNetEnv.Env.NoClobber().Load(dotEnv);
        var baseConnection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings__DefaultConnection is not set (Stockify-Server/.env).");
        var builder = new NpgsqlConnectionStringBuilder(baseConnection);
        _adminConnection = new NpgsqlConnectionStringBuilder(baseConnection) { Database = "postgres" }.ConnectionString;
        builder.Database = _databaseName;
        var testConnection = builder.ConnectionString;
        _testConnection = testConnection;

        await using (var admin = new NpgsqlConnection(_adminConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        // Program.cs loads .env with NoClobber, so values set here win over the developer's .env.
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings__DefaultConnection"] = testConnection,
            ["JwtSettings__Key"] = "integration-test-signing-key-0123456789abcdef0123456789",
            ["PlatformAdmin__Email"] = PlatformEmail,
            ["PlatformAdmin__Password"] = PlatformPassword,
            ["RateLimiting__SignInPerMinute"] = "100000",
            ["Serilog__MinimumLevel__Default"] = "Warning",
        };
        foreach (var (key, value) in settings)
            Environment.SetEnvironmentVariable(key, value);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Development");
            host.ConfigureServices(services =>
            {
                using var scope = services.BuildServiceProvider().CreateScope();
                scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
            });
        });
        Client = _factory.CreateClient();
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetDirectoryName(path)!;

    private static string? FindDotEnv(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        if (_adminConnection is null) return;
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>Raw SQL against the throwaway test database, for states the API cannot create (e.g. an expired lot).</summary>
    public async Task ExecuteSqlAsync(string sql, params (string name, object value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_testConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A fresh HttpClient carrying the given bearer token.</summary>
    public HttpClient As(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public Task<string> LoginAsync(string email, string password = Password) =>
        TokenAsync("/api/auth/login", email, password);

    public Task<string> PlatformLoginAsync() =>
        TokenAsync("/api/platform/auth/login", PlatformEmail, PlatformPassword);

    private async Task<string> TokenAsync(string path, string email, string password)
    {
        var response = await Client.PostAsJsonAsync(path, new { email, password });
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"POST {path} as {email} -> {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.GetProperty("token").GetString()!;
    }

    /// <summary>Provisions a company through the platform API and returns a client signed in as its owner.</summary>
    public async Task<TestTenant> NewTenantAsync(string name, string businessType = "RETAIL", decimal taxRate = 0.11m, decimal? secondCurrencyRate = null)
    {
        var slug = $"{name.ToLowerInvariant().Replace(' ', '-')}-{Guid.NewGuid():N}";
        slug = slug.Length > 40 ? slug[..40] : slug;
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.local";
        using var platform = As(await PlatformLoginAsync());
        var response = await platform.PostAsJsonAsync("/api/platform/tenants", new
        {
            companyName = name,
            slug,
            businessType,
            firstBranchName = "Main",
            ownerName = "Owner",
            ownerEmail,
            ownerPassword = Password,
            baseCurrency = new { code = "USD", name = "US Dollar", symbol = "$", exchangeRate = 1 },
            additionalCurrencies = secondCurrencyRate is decimal rate
                ? new[] { new { code = "LBP", name = "Lebanese Pound", symbol = "LL", exchangeRate = rate } }
                : Array.Empty<object>(),
            taxName = "VAT",
            taxRate,
        });
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = await LoginAsync(ownerEmail);
        return new TestTenant(this, created.GetProperty("id").GetInt32(), created.GetProperty("firstBranchId").GetInt32(), ownerEmail, As(token));
    }
}

/// <summary>A provisioned company with helpers to build the few records most tests need.</summary>
public sealed class TestTenant
{
    private readonly ApiFixture _fixture;

    public TestTenant(ApiFixture fixture, int tenantId, int branchId, string ownerEmail, HttpClient owner)
    {
        _fixture = fixture;
        TenantId = tenantId;
        BranchId = branchId;
        OwnerEmail = ownerEmail;
        Owner = owner;
    }

    public int TenantId { get; }
    public int BranchId { get; }
    public string OwnerEmail { get; }
    /// <summary>Signed in as the company owner (every permission).</summary>
    public HttpClient Owner { get; }

    /// <summary>A sellable variant with <paramref name="stock"/> units in the main branch; returns (variantId, lotId, barcode).</summary>
    public async Task<(int variantId, int lotId, string barcode)> VariantWithStockAsync(decimal price, int stock, string? name = null)
    {
        name ??= $"Item {Guid.NewGuid():N}"[..20];
        var brandId = await IdOf(await Owner.PostAsJsonAsync("/api/brands", new { name = $"Brand {Guid.NewGuid():N}"[..20] }));
        var categoryId = await IdOf(await Owner.PostAsJsonAsync("/api/categories", new { name = $"Category {Guid.NewGuid():N}"[..20] }));
        var productId = await IdOf(await Owner.PostAsJsonAsync("/api/products", new { name, brandId, categoryId, attributeIds = Array.Empty<int>() }));
        var barcode = $"9{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";
        var variantId = await IdOf(await Owner.PostAsJsonAsync("/api/product-variants", new
        {
            productId, attributes = Array.Empty<object>(), price, netPrice = Math.Round(price / 2, 2), barcode,
        }));
        var lotId = await IdOf(await Owner.PostAsJsonAsync("/api/inventory-lots", new { variantId, lotNumber = $"LOT-{variantId}" }));
        if (stock > 0)
        {
            var adjust = await Owner.PostAsJsonAsync("/api/inventory-adjustments", new
            {
                variantId, branchId = BranchId, inventoryLotId = lotId, quantity = stock, reason = "Opening stock",
            });
            adjust.EnsureSuccessStatusCode();
        }
        return (variantId, lotId, barcode);
    }

    /// <summary>An active promotion (PERCENTAGE or FIXED_AMOUNT) returning its id.</summary>
    public async Task<int> SaleAsync(string typeCode, decimal value)
    {
        var saleTypeId = await LookupIdAsync("saleTypes", typeCode);
        return await IdOf(await Owner.PostAsJsonAsync("/api/sales", new
        {
            name = $"Sale {Guid.NewGuid():N}"[..16], saleTypeId, value,
            startDate = DateTime.UtcNow.AddDays(-1), endDate = DateTime.UtcNow.AddDays(1),
        }));
    }

    public async Task AssignSaleAsync(int variantId, int saleId)
    {
        var response = await Owner.PatchAsJsonAsync($"/api/product-variants/{variantId}", new { saleId });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Places an order with an order-level discount; returns (orderId, invoiceId).</summary>
    public async Task<(int orderId, int invoiceId)> OrderWithDiscountAsync(string discountTypeCode, decimal discountValue, params (int variantId, int quantity)[] lines)
    {
        var response = await Owner.PostAsJsonAsync("/api/orders", new
        {
            customerId = await WalkInCustomerIdAsync(),
            deliveryLocationId = await InStoreLocationIdAsync(),
            createdByEmployeeId = await SalespersonAsync(),
            discountTypeId = await LookupIdAsync("discountTypes", discountTypeCode),
            discountValue,
            items = lines.Select(l => new { productVariantId = l.variantId, quantity = l.quantity }),
        });
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"POST /api/orders -> {(int)response.StatusCode}: {text}");
        var body = JsonDocument.Parse(text).RootElement;
        return (body.GetProperty("orderId").GetInt32(), body.GetProperty("invoiceId").GetInt32());
    }

    /// <summary>A salesperson at the main branch.</summary>
    public async Task<int> SalespersonAsync() =>
        await IdOf(await Owner.PostAsJsonAsync("/api/employees", new { name = "Cashier", branchId = BranchId, isSale = true, email = $"c-{Guid.NewGuid():N}@test.local" }));

    public async Task<int> WalkInCustomerIdAsync()
    {
        var list = await Owner.GetFromJsonAsync<JsonElement>("/api/customers?page=1");
        return list.GetProperty("customers").EnumerateArray().First().GetProperty("id").GetInt32();
    }

    public async Task<int> InStoreLocationIdAsync()
    {
        var list = await Owner.GetFromJsonAsync<JsonElement>("/api/delivery-charges");
        var rows = list.ValueKind == JsonValueKind.Array ? list.EnumerateArray() : list.GetProperty("items").EnumerateArray();
        return rows.First(r => r.GetProperty("isInStore").GetBoolean()).GetProperty("id").GetInt32();
    }

    public async Task<int> BaseCurrencyIdAsync()
    {
        var list = await Owner.GetFromJsonAsync<JsonElement>("/api/currencies");
        return list.EnumerateArray().First(c => c.GetProperty("exchangeRate").GetDecimal() == 1).GetProperty("id").GetInt32();
    }

    public async Task<int> LookupIdAsync(string list, string code)
    {
        var lookups = await Owner.GetFromJsonAsync<JsonElement>("/api/lookups");
        return lookups.GetProperty(list).EnumerateArray().First(r => r.GetProperty("code").GetString() == code).GetProperty("id").GetInt32();
    }

    /// <summary>Places an order for the given lines through the owner and returns (orderId, invoiceId).</summary>
    public async Task<(int orderId, int invoiceId)> OrderAsync(params (int variantId, int quantity)[] lines)
    {
        var response = await Owner.PostAsJsonAsync("/api/orders", new
        {
            customerId = await WalkInCustomerIdAsync(),
            deliveryLocationId = await InStoreLocationIdAsync(),
            createdByEmployeeId = await SalespersonAsync(),
            discountTypeId = (int?)null,
            discountValue = 0,
            items = lines.Select(l => new { productVariantId = l.variantId, quantity = l.quantity }),
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("orderId").GetInt32(), body.GetProperty("invoiceId").GetInt32());
    }

    /// <summary>Creates a user with the given role (by name, e.g. "PointOfSales") and permission keys; returns a client signed in as them.</summary>
    public Task<HttpClient> UserAsync(string roleName, params string[] permissionKeys) =>
        UserAsync(roleName, permissionKeys, BranchId);

    public async Task<HttpClient> UserAsync(string roleName, string[] permissionKeys, int branchId)
    {
        var roles = await Owner.GetFromJsonAsync<JsonElement>("/api/users/roles");
        var roleId = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == roleName).GetProperty("id").GetInt32();
        var email = $"user-{Guid.NewGuid():N}@test.local";
        var userId = await IdOf(await Owner.PostAsJsonAsync("/api/users", new { email, username = "User", password = ApiFixture.Password, roleId, branchId }));
        var permissions = await Owner.GetFromJsonAsync<JsonElement>($"/api/users/{userId}/permissions");
        var items = permissions.GetProperty("permissions").EnumerateArray()
            .Select(p => new { permissionId = p.GetProperty("id").GetInt32(), isActive = permissionKeys.Contains(p.GetProperty("key").GetString()) });
        var update = await Owner.PutAsJsonAsync($"/api/users/{userId}/permissions", new { items });
        update.EnsureSuccessStatusCode();
        return _fixture.As(await _fixture.LoginAsync(email));
    }

    /// <summary>Creates a branch and returns its id (the endpoint answers <c>true</c>, so the id is looked up by name).</summary>
    public async Task<int> BranchAsync(string name)
    {
        (await Owner.PostAsJsonAsync("/api/branches", new { name })).EnsureSuccessStatusCode();
        var branches = await Owner.GetFromJsonAsync<JsonElement>("/api/branches");
        var rows = branches.ValueKind == JsonValueKind.Array ? branches.EnumerateArray() : branches.GetProperty("branches").EnumerateArray();
        return rows.First(b => b.GetProperty("name").GetString() == name).GetProperty("id").GetInt32();
    }

    /// <summary>Units of a variant on hand at a branch (0 when the branch has no row).</summary>
    public async Task<int> StockAtAsync(int variantId, int branchId)
    {
        var body = await Owner.GetFromJsonAsync<JsonElement>($"/api/product-variants/{variantId}/location-stock");
        var row = body.GetProperty("stockPerBranch").EnumerateArray()
            .FirstOrDefault(s => s.GetProperty("branchId").GetInt32() == branchId);
        return row.ValueKind == JsonValueKind.Undefined ? 0 : row.GetProperty("stockQuantity").GetInt32();
    }

    public static async Task<int> IdOf(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} -> {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetInt32();
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
