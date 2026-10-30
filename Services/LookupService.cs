using Microsoft.Extensions.Caching.Memory;

namespace Stockify.Services;

/// <summary>Reads lookup tables (statuses, types, methods). Rows are global and cached in memory.</summary>
public sealed class LookupService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public LookupService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlyList<TLookup>> ListAsync<TLookup>() where TLookup : LookupEntity
    {
        var rows = await _cache.GetOrCreateAsync($"lookup:{typeof(TLookup).Name}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _db.Set<TLookup>()
                .AsNoTracking()
                .OrderBy(row => row.SortOrder)
                .ThenBy(row => row.Id)
                .ToListAsync();
        });
        return rows!;
    }

    public async Task<int> IdAsync<TLookup>(string code) where TLookup : LookupEntity
    {
        var row = (await ListAsync<TLookup>()).FirstOrDefault(item => item.Code == code)
            ?? throw new InvalidOperationException($"{typeof(TLookup).Name} '{code}' is missing from its lookup table.");
        return row.Id;
    }

    public async Task<TLookup?> FindAsync<TLookup>(int id) where TLookup : LookupEntity =>
        (await ListAsync<TLookup>()).FirstOrDefault(item => item.Id == id);

    public async Task<object> GetAllAsync() => new
    {
        orderStatuses = Project(await ListAsync<OrderStatus>()),
        orderTypes = Project(await ListAsync<OrderType>()),
        invoiceStatuses = Project(await ListAsync<InvoiceStatus>()),
        paymentMethods = Project(await ListAsync<PaymentMethod>()),
        movementTypes = Project(await ListAsync<MovementType>()),
        stockTransferStatuses = Project(await ListAsync<StockTransferStatus>()),
        saleTypes = Project(await ListAsync<SaleType>()),
        discountTypes = Project(await ListAsync<DiscountType>()),
        businessTypes = Project(await ListAsync<BusinessType>()),
        expenseLineTypes = Project(await ListAsync<ExpenseLineType>()),
        expenseStatuses = Project(await ListAsync<ExpenseStatus>())
    };

    private static IEnumerable<object> Project<TLookup>(IEnumerable<TLookup> rows) where TLookup : LookupEntity =>
        rows.Select(row => new { row.Id, row.Code, row.Name, row.Tone, row.SortOrder });
}
