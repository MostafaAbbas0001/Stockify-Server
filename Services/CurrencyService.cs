namespace Stockify.Services;

public class CurrencyService
{
    private readonly AppDbContext _db;

    public CurrencyService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<object>> GetAllAsync()
    {
        return await _db.Currencies
            .AsNoTracking()
            .Where(c => c.IsActive)
            // Base currency (rate 1) first, so every picker defaults to it.
            .OrderByDescending(c => c.ExchangeRate == 1)
            .ThenBy(c => c.Code)
            .Select(c => new
            {
                c.Id,
                c.Code,
                c.Name,
                c.Symbol,
                c.ExchangeRate,
                c.IsActive
            })
            .ToListAsync();
    }
}
