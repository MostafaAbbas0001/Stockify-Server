namespace Stockify.Services;

public class ProductService
{
    private const int PageSize = 20;
    private readonly AppDbContext _db;
    private readonly ProductVariantSkuService _skuService;

    public ProductService(AppDbContext db, ProductVariantSkuService skuService)
    {
        _db = db;
        _skuService = skuService;
    }

    public async Task<object> CreateAsync(CreateProductRequest dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Product data are invalid.");
        await ValidateBrandAndCategory(dto.BrandId, dto.CategoryId);
        var attributes = await ValidateAttributes(dto.AttributeIds);

        var product = new Product
        {
            Name = dto.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            ImageUrl = NormalizeOptional(dto.ImageUrl),
            BrandId = dto.BrandId,
            CategoryId = dto.CategoryId,
            Attributes = attributes
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return await GetByIdAsync(product.Id);
    }

    public async Task<object> UpdateAsync(int id, UpdateProductRequest dto)
    {
        if (dto == null || id <= 0)
            throw new ArgumentException("Invalid product.");

        var product = await _db.Products
            .Include(item => item.Attributes)
            .FirstOrDefaultAsync(item => item.Id == id)
            ?? throw new KeyNotFoundException("Product not found.");

        var name = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.");
        if (await _db.Products.AnyAsync(item => item.Id != id && item.Name == name))
            throw new InvalidOperationException("Another product with the same name already exists.");

        await ValidateBrandAndCategory(dto.BrandId, dto.CategoryId);

        product.Name = name;
        product.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        product.ImageUrl = NormalizeOptional(dto.ImageUrl);
        product.BrandId = dto.BrandId;
        product.Brand = await _db.Brands.FindAsync(dto.BrandId)
            ?? throw new ArgumentException("Brand does not exist.");
        product.CategoryId = dto.CategoryId;

        if (dto.AttributeIds != null)
            await ReplaceAttributes(product, dto.AttributeIds);

        await _skuService.RefreshForProductAsync(product.Id);
        await _db.SaveChangesAsync();
        return await GetByIdAsync(product.Id);
    }

    public async Task<object> SetAttributesAsync(
        int productId,
        IReadOnlyCollection<int> attributeIds)
    {
        var product = await _db.Products
            .Include(item => item.Attributes)
            .FirstOrDefaultAsync(item => item.Id == productId)
            ?? throw new KeyNotFoundException("Product not found.");

        await ReplaceAttributes(product, attributeIds);
        await _skuService.RefreshForProductAsync(product.Id);
        await _db.SaveChangesAsync();
        return await GetByIdAsync(productId);
    }

    public async Task<object> GetAllAsync(
        string? search,
        int? brandId,
        int? categoryId,
        int page)
    {
        page = Math.Max(page, 1);
        var query = _db.Products.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().ToLower()}%";
            query = query.Where(product =>
                EF.Functions.Like(product.Name.ToLower(), pattern) ||
                (product.Description != null && EF.Functions.Like(product.Description.ToLower(), pattern)) ||
                EF.Functions.Like(product.Brand.Name.ToLower(), pattern) ||
                EF.Functions.Like(product.Category.Name.ToLower(), pattern));
        }

        if (brandId.HasValue)
            query = query.Where(product => product.BrandId == brandId.Value);
        if (categoryId.HasValue)
            query = query.Where(product => product.CategoryId == categoryId.Value);
        var totalCount = await query.CountAsync();
        var products = await query
            .OrderByDescending(product => product.CreatedAt)
            .ThenByDescending(product => product.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(product => new
            {
                product.Id,
                product.Name,
                product.Description,
                product.ImageUrl,
                product.BrandId,
                BrandName = product.Brand.Name,
                product.CategoryId,
                CategoryName = product.Category.Name,
                product.CreatedAt,
                Attributes = product.Attributes
                    .OrderBy(attribute => attribute.Name)
                    .Select(attribute => new
                    {
                        AttributeId = attribute.Id,
                        attribute.Name,
                        Values = attribute.Values
                            .OrderBy(value => value.Value)
                            .Select(value => new { value.Id, value.Value })
                            .ToList()
                    })
                    .ToList()
            })
            .ToListAsync();

        return new
        {
            totalCount,
            page,
            perPage = PageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)PageSize),
            products
        };
    }

    public async Task<object> GetByIdAsync(int id)
    {
        var product = await _db.Products
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Description,
                item.ImageUrl,
                item.BrandId,
                BrandName = item.Brand.Name,
                item.CategoryId,
                CategoryName = item.Category.Name,
                Attributes = item.Attributes
                    .OrderBy(attribute => attribute.Name)
                    .Select(attribute => new
                    {
                        AttributeId = attribute.Id,
                        attribute.Name,
                        Values = attribute.Values
                            .OrderBy(value => value.Value)
                            .Select(value => new { value.Id, value.Value })
                            .ToList()
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        return product ?? throw new KeyNotFoundException("Product not found.");
    }

    public async Task<object> GetByProductAsync(int productId, string? search, int page)
    {
        page = Math.Max(page, 1);
        var query = _db.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.ProductId == productId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().ToLower()}%";
            query = query.Where(variant =>
                EF.Functions.Like(variant.SKU.ToLower(), pattern) ||
                (variant.Barcode != null && EF.Functions.Like(variant.Barcode.ToLower(), pattern)) ||
                variant.AttributeValues.Any(value =>
                    EF.Functions.Like(value.Value.ToLower(), pattern)));
        }

        var totalCount = await query.CountAsync();
        var variants = await query
            .OrderByDescending(variant => variant.CreatedAt)
            .ThenByDescending(variant => variant.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(variant => new
            {
                variant.Id,
                variant.SKU,
                variant.ImageUrl,
                ProductImageUrl = variant.Product.ImageUrl,
                ProductName = variant.Product.Name,
                BrandName = variant.Product.Brand.Name,
                ProductCategoryId = variant.Product.CategoryId,
                CategoryName = variant.Product.Category.Name,
                variant.Price,
                variant.NetPrice,
                variant.CreatedAt,
                Attributes = variant.AttributeValues
                    .OrderBy(value => value.ProductAttribute.Name)
                    .Select(value => new
                    {
                        AttributeId = value.ProductAttributeId,
                        Name = value.ProductAttribute.Name,
                        ValueId = value.Id,
                        value.Value
                    })
                    .ToList()
            })
            .ToListAsync();

        return new
        {
            totalCount,
            page,
            perPage = PageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)PageSize),
            variants
        };
    }

    public async Task<object> GetProductsAndRelatedVariantsAsync(
        string? search,
        int? brandId,
        int? categoryId,
        int page)
    {
        page = Math.Max(page, 1);
        var query = _db.Products.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().ToLower()}%";
            query = query.Where(product =>
                EF.Functions.Like(product.Name.ToLower(), pattern) ||
                product.ProductVariants.Any(variant =>
                    EF.Functions.Like(variant.SKU.ToLower(), pattern) ||
                    (variant.Barcode != null && EF.Functions.Like(variant.Barcode.ToLower(), pattern))));
        }

        if (brandId.HasValue)
            query = query.Where(product => product.BrandId == brandId.Value);
        if (categoryId.HasValue)
            query = query.Where(product => product.CategoryId == categoryId.Value);

        var totalCount = await query.CountAsync();
        var products = await query
            .OrderByDescending(product => product.CreatedAt)
            .ThenByDescending(product => product.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(product => new
            {
                ProductId = product.Id,
                ProductName = product.Name,
                product.ImageUrl,
                product.CreatedAt,
                Variants = product.ProductVariants.Select(variant => new
                {
                    VariantId = variant.Id,
                    variant.SKU,
                    variant.ImageUrl,
                    variant.SaleId,
                    Attributes = variant.AttributeValues
                        .OrderBy(value => value.ProductAttribute.Name)
                        .Select(value => new
                        {
                            AttributeId = value.ProductAttributeId,
                            Name = value.ProductAttribute.Name,
                            ValueId = value.Id,
                            value.Value
                        })
                        .ToList()
                }).ToList()
            })
            .ToListAsync();

        return new
        {
            totalCount,
            page,
            perPage = PageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)PageSize),
            products
        };
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var product = await _db.Products.FindAsync(id)
            ?? throw new KeyNotFoundException("Product not found.");

        if (await _db.ProductVariants.AnyAsync(variant => variant.ProductId == id))
            throw new InvalidOperationException(
                "Cannot delete this product because it has variants. Please delete its variants first.");

        _db.Products.Remove(product);
        await _db.SaveChangesAsync();
        return true;
    }

    private async Task ReplaceAttributes(
        Product product,
        IReadOnlyCollection<int> attributeIds)
    {
        var attributes = await ValidateAttributes(attributeIds);
        var requestedIds = attributes.Select(attribute => attribute.Id).ToHashSet();
        var currentIds = product.Attributes.Select(attribute => attribute.Id).ToHashSet();

        var variants = await _db.ProductVariants
            .Include(variant => variant.AttributeValues)
                .ThenInclude(value => value.ProductAttribute)
            .Where(variant => variant.ProductId == product.Id)
            .ToListAsync();

        var removedIds = currentIds.Except(requestedIds).ToHashSet();
        if (removedIds.Count > 0)
        {
            foreach (var variant in variants)
            {
                foreach (var value in variant.AttributeValues
                             .Where(value => removedIds.Contains(value.ProductAttributeId))
                             .ToList())
                    variant.AttributeValues.Remove(value);
            }
        }

        product.Attributes.Clear();
        foreach (var attribute in attributes)
            product.Attributes.Add(attribute);
    }

    private async Task<List<ProductAttribute>> ValidateAttributes(
        IReadOnlyCollection<int>? requestedAttributeIds)
    {
        var attributeIds = requestedAttributeIds?.ToList() ?? new List<int>();

        if (attributeIds.Any(attributeId => attributeId <= 0))
            throw new ArgumentException("Attribute IDs must be greater than zero.");

        if (attributeIds.Distinct().Count() != attributeIds.Count)
            throw new ArgumentException("A product cannot contain the same attribute more than once.");

        var attributes = await _db.ProductAttributes
            .Where(attribute => attributeIds.Contains(attribute.Id))
            .ToListAsync();
        if (attributes.Count != attributeIds.Count)
            throw new ArgumentException("One or more attributes do not exist.");

        return attributes;
    }

    private async Task ValidateBrandAndCategory(int brandId, int categoryId)
    {
        if (!await _db.Brands.AnyAsync(brand => brand.Id == brandId))
            throw new ArgumentException("Brand does not exist.");

        if (!await _db.Categories.AnyAsync(category => category.Id == categoryId))
            throw new ArgumentException("Category does not exist.");
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
