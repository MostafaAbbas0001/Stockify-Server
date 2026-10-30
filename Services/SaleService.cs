namespace Stockify.Services;

public class SaleService
{
    private const int PageSize = 20;
    private readonly AppDbContext _db;
    private readonly LookupService _lookups;

    public SaleService(AppDbContext db, LookupService lookups)
    {
        _db = db;
        _lookups = lookups;
    }

    public async Task<object> GetAllSalesAsync(
        string? search,
        int? saleTypeId,
        bool? isActive,
        DateTime? startDate,
        DateTime? endDate,
        int page)
    {
        page = Math.Max(page, 1);
        var now = DateTime.UtcNow;
        var query = _db.Sales
            .AsNoTracking()
            .Where(sale => !sale.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().ToLower()}%";
            query = query.Where(sale =>
                EF.Functions.Like(sale.Name.ToLower(), pattern));
        }
        if (saleTypeId.HasValue)
            query = query.Where(sale => sale.SaleTypeId == saleTypeId.Value);
        if (isActive.HasValue)
        {
            query = isActive.Value
                ? query.Where(sale =>
                    (!sale.StartDate.HasValue || sale.StartDate <= now) &&
                    (!sale.EndDate.HasValue || sale.EndDate >= now))
                : query.Where(sale =>
                    (sale.StartDate.HasValue && sale.StartDate > now) ||
                    (sale.EndDate.HasValue && sale.EndDate < now));
        }
        if (startDate.HasValue)
            query = query.Where(sale => sale.StartDate >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(sale => sale.EndDate <= endDate.Value);

        var totalCount = await query.CountAsync();
        var sales = await query
            .OrderByDescending(sale => sale.CreatedAt)
            .ThenByDescending(sale => sale.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(sale => new
            {
                sale.Id,
                sale.Name,
                sale.SaleTypeId,
                SaleTypeCode = sale.SaleType.Code,
                sale.Value,
                sale.StartDate,
                sale.EndDate,
                sale.CreatedAt,
                IsActiveNow =
                    (!sale.StartDate.HasValue || sale.StartDate <= now) &&
                    (!sale.EndDate.HasValue || sale.EndDate >= now),
                VariantCount = _db.ProductVariants.Count(variant =>
                    variant.SaleId == sale.Id)
            })
            .ToListAsync();

        return new
        {
            totalCount,
            page,
            perPage = PageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)PageSize),
            sales
        };
    }

    public async Task<object> GetSaleDetailsAsync(
        int saleId,
        string? search,
        int page)
    {
        page = Math.Max(page, 1);
        var now = DateTime.UtcNow;

        var sale = await _db.Sales
            .AsNoTracking()
            .Where(s => s.Id == saleId)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.SaleTypeId,
                SaleTypeCode = s.SaleType.Code,
                s.Value,
                s.StartDate,
                s.EndDate,
                s.IsDeleted,
                IsActiveNow = !s.IsDeleted
                              && (!s.StartDate.HasValue || s.StartDate <= now)
                              && (!s.EndDate.HasValue || s.EndDate >= now)
            })
            .FirstOrDefaultAsync();

        if (sale == null)
            throw new KeyNotFoundException($"Sale with ID {saleId} not found.");

        var variantQuery = _db.ProductVariants
            .AsNoTracking()
            .Where(v => v.SaleId == saleId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().ToLower()}%";
            variantQuery = variantQuery.Where(variant =>
                EF.Functions.Like(variant.SKU.ToLower(), pattern) ||
                (variant.Barcode != null &&
                 EF.Functions.Like(variant.Barcode.ToLower(), pattern)) ||
                EF.Functions.Like(variant.Product.Name.ToLower(), pattern) ||
                variant.AttributeValues.Any(value =>
                    EF.Functions.Like(
                        value.Value.ToLower(),
                        pattern)));
        }

        var totalCount = await variantQuery.CountAsync();
        var variants = await variantQuery
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => v.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(v => new
            {
                v.Id,
                v.SKU,
                v.Barcode,
                ImageUrl = v.ImageUrl ?? v.Product.ImageUrl,
                BrandName = v.Product.Brand.Name,
                ProductName = v.Product.Name,
                Attributes = v.AttributeValues
                    .OrderBy(item => item.ProductAttribute.Name)
                    .Select(item => new
                    {
                        Name = item.ProductAttribute.Name,
                        item.Value
                    })
                    .ToList()
            })
            .ToListAsync();

        var result = new
        {
            sale.Id,
            sale.Name,
            sale.SaleTypeId,
            sale.SaleTypeCode,
            sale.Value,
            sale.StartDate,
            sale.EndDate,
            sale.IsDeleted,
            sale.IsActiveNow,
            totalCount,
            page,
            perPage = PageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)PageSize),
            variants
        };

        return result;
    }

    public async Task<IEnumerable<object>> SearchVariantsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<object>();

        var term = query.Trim().ToLower();

        var results = await _db.ProductVariants
            .AsNoTracking()
            .Include(v => v.Sale)
            .Include(v => v.Product)
            .Include(v => v.AttributeValues)
                .ThenInclude(value => value.ProductAttribute)
            .Where(v => v.SaleId != null)
            .Where(v =>
                v.SKU.ToLower().Contains(term)
                || (v.Barcode != null && v.Barcode.ToLower().Contains(term))
                || v.Id.ToString() == term)
            .OrderBy(v => v.SKU)
            .Take(PageSize)
            .ToListAsync();

        return results.Select(v => new
        {
            Variant = new
            {
                v.Id,
                SKU = v.SKU,
                Barcode = v.Barcode,
                Price = v.Price,
                ImageUrl = v.ImageUrl ?? v.Product.ImageUrl,
                ProductName = v.Product.Name,
                Attributes = v.AttributeValues
                    .OrderBy(item => item.ProductAttribute.Name)
                    .Select(item => new
                    {
                        Name = item.ProductAttribute.Name,
                        item.Value
                    })
            },
            Sale = v.Sale != null ? new { v.Sale.Id, v.Sale.Name } : null
        });
    }

    public async Task<SaleResponse> CreateSaleAsync(CreateSaleRequest request)
    {
        if (!request.StartDate.HasValue || !request.EndDate.HasValue)
            throw new ArgumentException("StartDate and EndDate are required.");

        if (request.EndDate < request.StartDate)
            throw new ArgumentException("EndDate cannot be earlier than StartDate.");

        var saleType = await RequireSaleTypeAsync(request.SaleTypeId);
        var sale = new Sale
        {
            Name = request.Name.Trim(),
            SaleTypeId = saleType.Id,
            Value = request.Value,
            StartDate = NormalizeDateToUtc(request.StartDate),
            EndDate = NormalizeDateToUtc(request.EndDate),
            CreatedAt = DateTime.UtcNow
        };

        _db.Sales.Add(sale);
        await _db.SaveChangesAsync();

        return ToResponse(sale, saleType);
    }

    public async Task<SaleResponse> UpdateSaleAsync(int id, UpdateSaleRequest request)
    {
        if (!request.StartDate.HasValue || !request.EndDate.HasValue)
            throw new ArgumentException("StartDate and EndDate are required.");
        if (request.EndDate < request.StartDate)
            throw new ArgumentException("EndDate cannot be earlier than StartDate.");

        var existing = await _db.Sales.FirstOrDefaultAsync(s => s.Id == id);
        if (existing == null)
            throw new KeyNotFoundException($"Sale with ID {id} not found.");

        var saleType = await RequireSaleTypeAsync(request.SaleTypeId);
        existing.Name = request.Name.Trim();
        existing.SaleTypeId = saleType.Id;
        existing.Value = request.Value;
        existing.StartDate = NormalizeDateToUtc(request.StartDate);
        existing.EndDate = NormalizeDateToUtc(request.EndDate);

        await _db.SaveChangesAsync();
        return ToResponse(existing, saleType);
    }

    private async Task<SaleType> RequireSaleTypeAsync(int saleTypeId) =>
        await _lookups.FindAsync<SaleType>(saleTypeId)
            ?? throw new ArgumentException($"Sale type {saleTypeId} does not exist.");

    private static SaleResponse ToResponse(Sale sale, SaleType saleType) => new(
        sale.Id,
        sale.Name,
        sale.SaleTypeId,
        saleType.Code,
        sale.Value,
        sale.StartDate,
        sale.EndDate,
        sale.IsDeleted,
        sale.CreatedAt);

    public async Task<bool> DeleteSaleAsync(int id)
    {
        var sale = await _db.Sales.FindAsync(id);
        if (sale == null)
            throw new KeyNotFoundException($"Sale with ID {id} not found.");

        // Variants that referenced this sale go back to having no sale.
        var affectedVariants = await _db.ProductVariants
            .Where(pv => pv.SaleId == id)
            .ToListAsync();

        if (affectedVariants.Count > 0)
        {
            foreach (var variant in affectedVariants)
            {
                variant.SaleId = null;
            }
        }

        sale.IsDeleted = true;
        await _db.SaveChangesAsync();
        return true;
    }

    private static DateTime? NormalizeDateToUtc(DateTime? date)
    {
        if (!date.HasValue)
            return null;
        if (date.Value.Kind == DateTimeKind.Unspecified)
            return DateTime.SpecifyKind(
                date.Value,
                DateTimeKind.Local).ToUniversalTime();
        return date.Value.Kind == DateTimeKind.Local
            ? date.Value.ToUniversalTime()
            : date.Value;
    }
}
