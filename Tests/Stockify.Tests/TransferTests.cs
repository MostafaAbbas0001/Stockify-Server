using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Stockify.Tests;

/// <summary>Stock transfers between two branches: request → send → receive → resolve, cancel, and races.</summary>
[Collection(nameof(ApiCollection))]
public class TransferTests
{
    private readonly ApiFixture _api;

    public TransferTests(ApiFixture api) => _api = api;

    private sealed record Setup(TestTenant Tenant, int MallId, int VariantId, int LotId);

    /// <summary>A company with a Mall branch and 10 units of one variant at the main branch.</summary>
    private async Task<Setup> TwoBranchesAsync()
    {
        var tenant = await _api.NewTenantAsync("Transfer Co");
        var mallId = await tenant.BranchAsync("Mall");
        var (variantId, lotId, _) = await tenant.VariantWithStockAsync(price: 10m, stock: 10);
        return new Setup(tenant, mallId, variantId, lotId);
    }

    private static async Task<(int transferId, int itemId)> RequestAsync(Setup s, int quantity)
    {
        var id = await TestTenant.IdOf(await s.Tenant.Owner.PostAsJsonAsync("/api/stock-transfers", new
        {
            sourceBranchId = s.Tenant.BranchId, destinationBranchId = s.MallId,
            items = new[] { new { productVariantId = s.VariantId, quantity } },
        }));
        var detail = await s.Tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/stock-transfers/{id}");
        return (id, detail.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetInt32());
    }

    private static Task<HttpResponseMessage> SendAsync(Setup s, int transferId, int itemId, int quantity) =>
        s.Tenant.Owner.PostAsJsonAsync($"/api/stock-transfers/{transferId}/dispatches", new
        {
            sourceBranchId = s.Tenant.BranchId,
            lots = new[] { new { stockTransferItemId = itemId, inventoryLotId = s.LotId, quantity } },
        });

    private static Task<HttpResponseMessage> ReceiveAsync(Setup s, int transferId, int itemId, int cumulative, bool complete = false) =>
        s.Tenant.Owner.PostAsJsonAsync($"/api/stock-transfers/{transferId}/receipts", new
        {
            complete,
            lots = new[] { new { stockTransferItemId = itemId, inventoryLotId = s.LotId, quantityReceived = cumulative } },
        });

    private static async Task<JsonElement> DetailAsync(Setup s, int transferId) =>
        await s.Tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/stock-transfers/{transferId}");

    [Fact]
    public async Task Full_transfer_moves_stock_and_completes()
    {
        var s = await TwoBranchesAsync();
        var (transferId, itemId) = await RequestAsync(s, 4);
        Assert.Equal("DRAFT", (await DetailAsync(s, transferId)).GetProperty("statusCode").GetString());

        (await SendAsync(s, transferId, itemId, 4)).EnsureSuccessStatusCode();
        Assert.Equal("SENT", (await DetailAsync(s, transferId)).GetProperty("statusCode").GetString());
        Assert.Equal(6, await s.Tenant.StockAtAsync(s.VariantId, s.Tenant.BranchId)); // left the main branch
        Assert.Equal(0, await s.Tenant.StockAtAsync(s.VariantId, s.MallId));          // in transit

        (await ReceiveAsync(s, transferId, itemId, 1)).EnsureSuccessStatusCode();
        Assert.Equal("PARTIALLY_RECEIVED", (await DetailAsync(s, transferId)).GetProperty("statusCode").GetString());
        (await ReceiveAsync(s, transferId, itemId, 4)).EnsureSuccessStatusCode(); // cumulative quantity

        var detail = await DetailAsync(s, transferId);
        Assert.Equal("COMPLETED", detail.GetProperty("statusCode").GetString());
        Assert.Equal(6, await s.Tenant.StockAtAsync(s.VariantId, s.Tenant.BranchId));
        Assert.Equal(4, await s.Tenant.StockAtAsync(s.VariantId, s.MallId));

        // The ledger shows the move: out of main, into the mall.
        var movements = await s.Tenant.Owner.GetFromJsonAsync<JsonElement>($"/api/stock-movements?page=1&variantId={s.VariantId}");
        var types = movements.EnumerateObject().First(p => p.Value.ValueKind == JsonValueKind.Array).Value
            .EnumerateArray().Select(m => m.TryGetProperty("movementTypeCode", out var c) ? c.GetString() : m.GetProperty("typeCode").GetString()).ToList();
        Assert.Contains("TRANSFER_OUT", types);
        Assert.Contains("TRANSFER_IN", types);
    }

    [Fact]
    public async Task Short_delivery_is_a_discrepancy_until_resolved()
    {
        var s = await TwoBranchesAsync();
        var (transferId, itemId) = await RequestAsync(s, 5);
        (await SendAsync(s, transferId, itemId, 5)).EnsureSuccessStatusCode();
        (await ReceiveAsync(s, transferId, itemId, 3, complete: true)).EnsureSuccessStatusCode();

        var detail = await DetailAsync(s, transferId);
        Assert.Equal("COMPLETED", detail.GetProperty("statusCode").GetString());
        Assert.Equal(2, detail.GetProperty("items").EnumerateArray().Single().GetProperty("discrepancy").GetInt32());
        Assert.Equal(3, await s.Tenant.StockAtAsync(s.VariantId, s.MallId));

        var resolve = await s.Tenant.Owner.PutAsJsonAsync($"/api/stock-transfers/{transferId}/discrepancy", new { note = "2 damaged in transit" });
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        var again = await s.Tenant.Owner.PutAsJsonAsync($"/api/stock-transfers/{transferId}/discrepancy", new { note = "again" });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task Cancelling_a_sent_transfer_returns_the_unreceived_units()
    {
        var s = await TwoBranchesAsync();
        var (transferId, itemId) = await RequestAsync(s, 4);
        (await SendAsync(s, transferId, itemId, 4)).EnsureSuccessStatusCode();
        (await ReceiveAsync(s, transferId, itemId, 1)).EnsureSuccessStatusCode();

        var cancel = await s.Tenant.Owner.DeleteAsync($"/api/stock-transfers/{transferId}?reason=wrong%20branch");
        Assert.True(cancel.IsSuccessStatusCode);

        Assert.Equal("CANCELLED", (await DetailAsync(s, transferId)).GetProperty("statusCode").GetString());
        Assert.Equal(9, await s.Tenant.StockAtAsync(s.VariantId, s.Tenant.BranchId)); // 3 unreceived came back
        Assert.Equal(1, await s.Tenant.StockAtAsync(s.VariantId, s.MallId));          // the received one stays
        Assert.Equal(HttpStatusCode.BadRequest, (await ReceiveAsync(s, transferId, itemId, 2)).StatusCode);
    }

    [Fact]
    public async Task Wrong_quantities_are_refused()
    {
        var s = await TwoBranchesAsync();
        var (transferId, itemId) = await RequestAsync(s, 3);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(s, transferId, itemId, 4)).StatusCode);  // more than requested
        Assert.Equal(HttpStatusCode.BadRequest, (await ReceiveAsync(s, transferId, itemId, 1)).StatusCode); // not sent yet

        var big = await RequestAsync(s, 50);
        var tooMuch = await SendAsync(s, big.transferId, big.itemId, 50); // only 10 in stock
        Assert.False(tooMuch.IsSuccessStatusCode);
        Assert.Equal(10, await s.Tenant.StockAtAsync(s.VariantId, s.Tenant.BranchId));

        (await SendAsync(s, transferId, itemId, 3)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await ReceiveAsync(s, transferId, itemId, 4)).StatusCode); // more than sent
        (await ReceiveAsync(s, transferId, itemId, 2)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await ReceiveAsync(s, transferId, itemId, 1)).StatusCode); // can't go down
    }

    [Fact]
    public async Task Double_send_deducts_stock_once()
    {
        var s = await TwoBranchesAsync();
        var (transferId, itemId) = await RequestAsync(s, 4);

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => SendAsync(s, transferId, itemId, 4)));

        Assert.Equal(1, results.Count(r => r.IsSuccessStatusCode));
        Assert.Equal(6, await s.Tenant.StockAtAsync(s.VariantId, s.Tenant.BranchId));
    }

    [Fact]
    public async Task Double_receive_adds_stock_once()
    {
        var s = await TwoBranchesAsync();
        var (transferId, itemId) = await RequestAsync(s, 4);
        (await SendAsync(s, transferId, itemId, 4)).EnsureSuccessStatusCode();

        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => ReceiveAsync(s, transferId, itemId, 4)));

        Assert.Equal(4, await s.Tenant.StockAtAsync(s.VariantId, s.MallId));
        Assert.Equal(6, await s.Tenant.StockAtAsync(s.VariantId, s.Tenant.BranchId));
    }

    [Fact]
    public async Task Double_cancel_returns_stock_once()
    {
        var s = await TwoBranchesAsync();
        var (transferId, itemId) = await RequestAsync(s, 4);
        (await SendAsync(s, transferId, itemId, 4)).EnsureSuccessStatusCode();

        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => s.Tenant.Owner.DeleteAsync($"/api/stock-transfers/{transferId}?reason=x")));

        Assert.Equal(10, await s.Tenant.StockAtAsync(s.VariantId, s.Tenant.BranchId));
    }

    [Fact]
    public async Task Cancel_racing_a_receipt_never_creates_stock()
    {
        var s = await TwoBranchesAsync();
        var (transferId, itemId) = await RequestAsync(s, 4);
        (await SendAsync(s, transferId, itemId, 4)).EnsureSuccessStatusCode();

        await Task.WhenAll(
            ReceiveAsync(s, transferId, itemId, 4),
            s.Tenant.Owner.DeleteAsync($"/api/stock-transfers/{transferId}?reason=x"));

        var total = await s.Tenant.StockAtAsync(s.VariantId, s.Tenant.BranchId) + await s.Tenant.StockAtAsync(s.VariantId, s.MallId);
        Assert.Equal(10, total); // the 4 units end up in exactly one place
    }
}
