namespace Stockify.Services;

public class TaxService
{
    private readonly AppDbContext _db;

    public TaxService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<object>> GetAllAsync()
    {
        var rates = await _db.TaxRates
            .AsNoTracking()
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Rate
            })
            .ToListAsync();

        return rates;
    }
}
