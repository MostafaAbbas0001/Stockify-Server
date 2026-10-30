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
            .OrderBy(c => c.Code)
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
