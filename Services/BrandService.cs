namespace Stockify.Services;

public class BrandService
{
    private readonly AppDbContext _db;
    private readonly ProductVariantSkuService _skuService;

    public BrandService(AppDbContext db, ProductVariantSkuService skuService)
    {
        _db = db;
        _skuService = skuService;
    }

    public async Task<Brand> CreateAsync(Brand brand)
    {
        if (brand == null || string.IsNullOrWhiteSpace(brand.Name))
            throw new ArgumentException("Brand data is invalid.");

        await _db.Brands.AddAsync(brand);
        await _db.SaveChangesAsync();

        return brand;
    }

    public async Task<Brand> UpdateAsync(int id, Brand updatedBrand)
    {
        var brand = await _db.Brands.FindAsync(id);
        if (brand == null)
            throw new KeyNotFoundException("Brand not found.");

        if (string.IsNullOrWhiteSpace(updatedBrand.Name))
            throw new ArgumentException("Brand name is invalid.");

        var name = updatedBrand.Name.Trim();
        if (await _db.Brands.AnyAsync(item =>
                item.Id != id && item.Name.ToUpper() == name.ToUpper()))
            throw new InvalidOperationException("Another brand with the same name already exists.");

        brand.Name = name;
        await _skuService.RefreshForBrandAsync(id);
        await _db.SaveChangesAsync();

        return brand;
    }

    public async Task<IEnumerable<object>> GetAllAsync()
    {
        var brands = await _db.Brands
            .AsNoTracking()
            .Select(b => new
            {
                b.Id,
                b.Name,
                ProductCount = _db.Products.Count(p => p.BrandId == b.Id)
            })
            .ToListAsync();

        return brands;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var brand = await _db.Brands.FindAsync(id);
        if (brand == null)
            throw new KeyNotFoundException("Brand not found.");

        var inUse = await _db.Products.AnyAsync(p => p.BrandId == id);
        if (inUse)
            throw new InvalidOperationException("Cannot delete this brand because one or more products use it. Please move or remove those products first.");

        _db.Brands.Remove(brand);

        await _db.SaveChangesAsync();
        return true;
    }
}
