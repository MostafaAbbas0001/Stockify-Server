namespace Stockify.Services;

public class ProductVariantService
{
    private const int PageSize = 20;
    private readonly AppDbContext _db;
    private readonly ProductVariantSkuService _skuService;

    public ProductVariantService(AppDbContext db, ProductVariantSkuService skuService)
    {
        _db = db;
        _skuService = skuService;
    }

    public async Task<object> CreateAsync(CreateProductVariantRequest dto)
    {
        if (dto is null)
            throw new ArgumentException("Variant data is invalid.");
        if (dto.Price <= 0)
            throw new ArgumentException("Price must be greater than zero.");
        if (dto.NetPrice < 0)
            throw new ArgumentException("Net price must be zero or greater.");

        var product = await _db.Products
            .AsNoTracking()
            .Where(item => item.Id == dto.ProductId)
            .Select(item => new
            {
                item.Id,
                ProductName = item.Name,
                BrandName = item.Brand.Name
            })
            .FirstOrDefaultAsync()
            ?? throw new ArgumentException("Product does not exist.");

        var values = await ValidateAttributeValues(dto.ProductId, dto.Attributes);
        var sku = _skuService.BuildSku(product.BrandName, product.ProductName, values);
        await _skuService.EnsureAvailableAsync(sku);

        // 0 or null means "no sale".
        int? saleId = dto.SaleId is > 0 ? dto.SaleId : null;
        if (saleId.HasValue &&
            !await _db.Sales.AnyAsync(sale => sale.Id == saleId.Value && !sale.IsDeleted))
            throw new ArgumentException("Sale does not exist.");

        var variant = new ProductVariant
        {
            ProductId = dto.ProductId,
            Price = dto.Price,
            NetPrice = dto.NetPrice,
            SaleId = saleId,
            SKU = sku,
            Barcode = NormalizeOptional(dto.Barcode),
            ImageUrl = NormalizeOptional(dto.ImageUrl),
            AttributeValues = values
        };
        variant.InventoryLots.Add(new InventoryLot
        {
            LotNumber = InventoryLotService.BuildDefaultLotNumber(sku),
            ReceivedAt = DateTime.UtcNow
        });

        await using var transaction = await _db.Database.BeginTransactionAsync();

        _db.ProductVariants.Add(variant);
        await _db.SaveChangesAsync();

        var branchIds = await _db.Branches.Select(branch => branch.Id).ToListAsync();
        _db.LocationStocks.AddRange(branchIds.Select(branchId => new LocationStock
        {
            BranchId = branchId,
            ProductVariantId = variant.Id,
            ReorderLevel = 0,
            LastUpdated = DateTime.UtcNow
        }));

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return new
        {
            variant.Id,
            variant.SKU,
            variant.Barcode,
            variant.ImageUrl,
            variant.Price,
            variant.NetPrice,
            Attributes = values
                .OrderBy(value => value.ProductAttribute.Name)
                .Select(value => new
                {
                    AttributeId = value.ProductAttributeId,
                    Name = value.ProductAttribute.Name,
                    ValueId = value.Id,
                    value.Value
                })
        };
    }

    public async Task<object> GetAllAsync(
        string? search,
        int? productId,
        int? brandId,
        int? categoryId,
        bool? isSellable,
        int page)
    {
        page = Math.Max(page, 1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var query = _db.ProductVariants.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().ToLower()}%";
            query = query.Where(variant =>
                EF.Functions.Like(variant.SKU.ToLower(), pattern) ||
                (variant.Barcode != null && EF.Functions.Like(variant.Barcode.ToLower(), pattern)) ||
                EF.Functions.Like(variant.Product.Name.ToLower(), pattern) ||
                variant.AttributeValues.Any(value =>
                    EF.Functions.Like(value.Value.ToLower(), pattern)));
        }

        if (productId.HasValue)
            query = query.Where(variant => variant.ProductId == productId.Value);
        if (brandId.HasValue)
            query = query.Where(variant => variant.Product.BrandId == brandId.Value);
        if (categoryId.HasValue)
            query = query.Where(variant => variant.Product.CategoryId == categoryId.Value);
        if (isSellable.HasValue)
            query = query.Where(variant => variant.IsSellable == isSellable.Value);

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
                ProductName = variant.Product.Name,
                BrandName = variant.Product.Brand.Name,
                CategoryName = variant.Product.Category.Name,
                SaleName = variant.Sale != null ? variant.Sale.Name : null,
                variant.Price,
                variant.NetPrice,
                variant.Barcode,
                variant.ImageUrl,
                ProductImageUrl = variant.Product.ImageUrl,
                variant.IsSellable,
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

        var variantIds = variants.Select(variant => variant.Id).ToArray();
        var branchQuantities = await _db.BranchLotStocks
            .AsNoTracking()
            .Where(stock =>
                variantIds.Contains(stock.InventoryLot.ProductVariantId) &&
                (!stock.InventoryLot.ExpiryDate.HasValue || stock.InventoryLot.ExpiryDate >= today))
            .GroupBy(stock => new
            {
                stock.InventoryLot.ProductVariantId,
                stock.BranchId
            })
            .Select(group => new
            {
                group.Key.ProductVariantId,
                group.Key.BranchId,
                Quantity = group.Sum(stock => stock.Quantity)
            })
            .ToListAsync();

        var quantityByBranch = branchQuantities.ToDictionary(
            stock => (stock.ProductVariantId, stock.BranchId),
            stock => stock.Quantity);
        var quantityByVariant = branchQuantities
            .GroupBy(stock => stock.ProductVariantId)
            .ToDictionary(group => group.Key, group => group.Sum(stock => stock.Quantity));

        var locationSettings = await _db.LocationStocks
            .AsNoTracking()
            .Where(stock =>
                variantIds.Contains(stock.ProductVariantId) &&
                !stock.Branch.IsDeleted)
            .Select(stock => new
            {
                stock.ProductVariantId,
                stock.BranchId,
                BranchName = stock.Branch.Name,
                stock.ReorderLevel
            })
            .ToListAsync();

        var rows = variants.Select(variant => new
        {
            variant.Id,
            variant.SKU,
            variant.ProductName,
            variant.BrandName,
            variant.CategoryName,
            variant.SaleName,
            variant.Price,
            variant.NetPrice,
            variant.Barcode,
            variant.ImageUrl,
            variant.ProductImageUrl,
            variant.IsSellable,
            variant.CreatedAt,
            variant.Attributes,
            StockQuantity = quantityByVariant.GetValueOrDefault(variant.Id),
            LowStockBranches = locationSettings
                .Where(stock => stock.ProductVariantId == variant.Id)
                .Select(stock => new
                {
                    stock.BranchId,
                    stock.BranchName,
                    StockQuantity = quantityByBranch.GetValueOrDefault(
                        (stock.ProductVariantId, stock.BranchId)),
                    stock.ReorderLevel
                })
                .Where(stock => stock.StockQuantity <= stock.ReorderLevel)
                .ToList()
        }).ToList();

        return new
        {
            totalCount,
            page,
            perPage = PageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)PageSize),
            variants = rows
        };
    }

    public async Task<object> GetVariantLocationStockAsync(int id)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var variant = await _db.ProductVariants
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new
            {
                item.Id,
                item.SKU,
                item.Barcode,
                ImageUrl = item.ImageUrl ?? item.Product.ImageUrl,
                item.Price,
                Attributes = item.AttributeValues
                    .OrderBy(value => value.ProductAttribute.Name)
                    .Select(value => new
                    {
                        Name = value.ProductAttribute.Name,
                        value.Value
                    })
                    .ToList(),
                Stocks = item.LocationStocks
                    .Where(stock => stock.Branch != null && !stock.Branch.IsDeleted)
                    .Select(stock => new
                    {
                        stock.BranchId,
                        BranchName = stock.Branch.Name,
                        StockQuantity = item.InventoryLots
                            .SelectMany(lot => lot.BranchStocks)
                            .Where(lotStock =>
                                lotStock.BranchId == stock.BranchId &&
                                (!lotStock.InventoryLot.ExpiryDate.HasValue ||
                                 lotStock.InventoryLot.ExpiryDate >= today))
                            .Sum(lotStock => lotStock.Quantity),
                        stock.ReorderLevel,
                        Lots = item.InventoryLots
                            .Where(lot => lot.BranchStocks.Any(branchStock =>
                                branchStock.BranchId == stock.BranchId && branchStock.Quantity > 0))
                            .OrderBy(lot => lot.ExpiryDate)
                            .Select(lot => new
                            {
                                lot.Id,
                                lot.LotNumber,
                                lot.ManufacturingDate,
                                lot.ExpiryDate,
                                Quantity = lot.BranchStocks
                                    .Where(branchStock => branchStock.BranchId == stock.BranchId)
                                    .Sum(branchStock => branchStock.Quantity)
                            })
                            .ToList()
                    })
                    .OrderBy(stock => stock.BranchName)
                    .ToList()
            })
            .FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException("Product variant not found.");

        return new
        {
            VariantId = variant.Id,
            variant.SKU,
            variant.Barcode,
            variant.Price,
            variant.Attributes,
            StockPerBranch = variant.Stocks.Select(stock => new
            {
                stock.BranchId,
                stock.BranchName,
                stock.StockQuantity,
                stock.ReorderLevel,
                IsLowStock = stock.StockQuantity <= stock.ReorderLevel
            })
        };
    }

    public async Task<object> UpdateAsync(
        int id,
        UpdateProductVariantRequest dto)
    {
        if (dto == null)
            throw new ArgumentException("Request body is required.");

        var hasUpdatableField =
            dto.Price.HasValue ||
            dto.NetPrice.HasValue ||
            dto.SaleId.HasValue ||
            dto.Barcode != null ||
            dto.ImageUrl != null ||
            dto.Attributes != null;

        if (!hasUpdatableField)
            throw new ArgumentException("No fields provided to update.");
        if (dto.Price.HasValue && dto.Price.Value <= 0)
            throw new ArgumentException("Price must be greater than zero.");
        if (dto.NetPrice.HasValue && dto.NetPrice.Value < 0)
            throw new ArgumentException("Net price must be zero or greater.");

        var variant = await _db.ProductVariants
            .Include(item => item.Product)
                .ThenInclude(product => product.Brand)
            .Include(item => item.Product)
                .ThenInclude(product => product.Attributes)
            .Include(item => item.AttributeValues)
                .ThenInclude(value => value.ProductAttribute)
            .FirstOrDefaultAsync(item => item.Id == id)
            ?? throw new KeyNotFoundException("Product variant not found.");

        List<ProductAttributeValue>? values = null;
        if (dto.Attributes != null)
        {
            values = await ValidateAttributeValues(variant.ProductId, dto.Attributes);
            var requestedIds = values.Select(value => value.Id).ToHashSet();
            var existingIds = variant.AttributeValues
                .Select(value => value.Id)
                .ToHashSet();

            foreach (var value in variant.AttributeValues
                         .Where(value => !requestedIds.Contains(value.Id))
                         .ToList())
                variant.AttributeValues.Remove(value);

            foreach (var valueId in requestedIds.Where(valueId => !existingIds.Contains(valueId)))
                variant.AttributeValues.Add(values.Single(value => value.Id == valueId));
        }

        var barcode = dto.Barcode != null ? NormalizeOptional(dto.Barcode) : variant.Barcode;
        await ValidateUniqueBarcode(barcode, id);

        // SaleId 0 removes the variant from its sale.
        if (dto.SaleId is > 0 &&
            !await _db.Sales.AnyAsync(sale => sale.Id == dto.SaleId.Value && !sale.IsDeleted))
            throw new KeyNotFoundException("Sale not found or is deleted.");

        await _skuService.RefreshVariantAsync(variant);
        variant.Barcode = barcode;
        if (dto.ImageUrl != null)
            variant.ImageUrl = NormalizeOptional(dto.ImageUrl);
        if (dto.SaleId.HasValue)
            variant.SaleId = dto.SaleId.Value > 0 ? dto.SaleId.Value : null;
        if (dto.Price.HasValue)
            variant.Price = dto.Price.Value;
        if (dto.NetPrice.HasValue)
            variant.NetPrice = dto.NetPrice.Value;

        await _db.SaveChangesAsync();

        return await _db.ProductVariants
            .AsNoTracking()
            .Where(item => item.Id == variant.Id)
            .Select(item => new
            {
                item.Id,
                item.ProductId,
                item.SaleId,
                item.SKU,
                item.Barcode,
                item.ImageUrl,
                ProductImageUrl = item.Product.ImageUrl,
                ProductName = item.Product.Name,
                BrandName = item.Product.Brand.Name,
                CategoryName = item.Product.Category.Name,
                item.Price,
                item.NetPrice,
                item.IsSellable,
                item.CreatedAt,
                Attributes = item.AttributeValues
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
            .SingleAsync();
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var variant = await _db.ProductVariants.FindAsync(id)
            ?? throw new KeyNotFoundException("Product variant not found.");

        if (await _db.BranchLotStocks.AnyAsync(stock =>
                stock.InventoryLot.ProductVariantId == id && stock.Quantity > 0))
            throw new InvalidOperationException(
                "Cannot delete this variant because it has stock on hand. Reduce the stock to 0 first.");

        var unusedLots = await _db.InventoryLots
            .Where(lot => lot.ProductVariantId == id)
            .ToListAsync();
        _db.InventoryLots.RemoveRange(unusedLots);
        _db.ProductVariants.Remove(variant);
        await _db.SaveChangesAsync();
        return true;
    }

    private async Task<List<ProductAttributeValue>> ValidateAttributeValues(
        int productId,
        IReadOnlyCollection<ProductVariantAttributeSelectionRequest>? requestedAttributes)
    {
        var selections = requestedAttributes?.ToList()
            ?? new List<ProductVariantAttributeSelectionRequest>();

        if (selections.Any(selection => selection.AttributeId <= 0 || selection.ValueId <= 0))
            throw new ArgumentException("Attribute and value IDs must be greater than zero.");

        if (selections.Select(selection => selection.AttributeId).Distinct().Count() != selections.Count)
            throw new ArgumentException("A variant can select only one value for each attribute.");

        if (selections.Select(selection => selection.ValueId).Distinct().Count() != selections.Count)
            throw new ArgumentException("A variant cannot select the same attribute value more than once.");

        var valueIds = selections.Select(selection => selection.ValueId).ToList();

        var assignedIds = await _db.Products
            .AsNoTracking()
            .Where(product => product.Id == productId)
            .SelectMany(product => product.Attributes.Select(attribute => attribute.Id))
            .ToListAsync();

        var values = await _db.ProductAttributeValues
            .Include(value => value.ProductAttribute)
            .Where(value => valueIds.Contains(value.Id))
            .ToListAsync();

        if (values.Count != valueIds.Count)
            throw new ArgumentException("One or more attribute values do not exist.");

        var attributeIdByValueId = values.ToDictionary(value => value.Id, value => value.ProductAttributeId);
        if (selections.Any(selection =>
                attributeIdByValueId[selection.ValueId] != selection.AttributeId))
            throw new ArgumentException("One or more selected values do not belong to the specified attribute.");

        var assignedIdSet = assignedIds.ToHashSet();
        if (values.Any(value => !assignedIdSet.Contains(value.ProductAttributeId)))
            throw new ArgumentException("A selected attribute is not assigned to this product.");

        var selectedIds = values.Select(value => value.ProductAttributeId).ToHashSet();
        var missingAttributes = assignedIds
            .Where(attributeId => !selectedIds.Contains(attributeId))
            .ToList();

        if (missingAttributes.Count > 0)
            throw new ArgumentException("Every product attribute must have a selected value.");

        return values;
    }

    private async Task ValidateUniqueBarcode(string? barcode, int? excludingId = null)
    {
        if (barcode != null &&
            await _db.ProductVariants.AnyAsync(variant =>
                variant.Id != excludingId && variant.Barcode == barcode))
            throw new InvalidOperationException("A product variant with this barcode already exists.");
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
