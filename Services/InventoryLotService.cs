namespace Stockify.Services;

public class InventoryLotService
{
    private const int PageSize = 20;
    private readonly AppDbContext _db;

    public InventoryLotService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> GetAvailableQuantityAsync(int branchId, int variantId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await _db.BranchLotStocks
            .Where(stock =>
                stock.BranchId == branchId &&
                stock.InventoryLot.ProductVariantId == variantId &&
                (!stock.InventoryLot.ExpiryDate.HasValue || stock.InventoryLot.ExpiryDate >= today))
            .SumAsync(stock => stock.Quantity);
    }

    public async Task<Dictionary<int, int>> GetAvailableQuantitiesAsync(
        int branchId,
        IReadOnlyCollection<int> variantIds)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await _db.BranchLotStocks
            .Where(stock =>
                stock.BranchId == branchId &&
                variantIds.Contains(stock.InventoryLot.ProductVariantId) &&
                (!stock.InventoryLot.ExpiryDate.HasValue || stock.InventoryLot.ExpiryDate >= today))
            .GroupBy(stock => stock.InventoryLot.ProductVariantId)
            .Select(group => new
            {
                VariantId = group.Key,
                Quantity = group.Sum(stock => stock.Quantity)
            })
            .ToDictionaryAsync(item => item.VariantId, item => item.Quantity);
    }

    public async Task<InventoryLot> CreateLotAsync(
        int variantId,
        string lotNumber,
        DateOnly? manufacturingDate,
        DateOnly? expiryDate)
    {
        if (!await _db.ProductVariants.AnyAsync(item => item.Id == variantId))
            throw new KeyNotFoundException("Variant not found.");

        var normalizedLotNumber = lotNumber.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedLotNumber))
            throw new ArgumentException("Lot number is required.");
        if (manufacturingDate.HasValue && expiryDate.HasValue && manufacturingDate > expiryDate)
            throw new ArgumentException("Manufacturing date cannot be after the expiry date.");
        if (expiryDate.HasValue && expiryDate < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentException("Expired lots cannot be created.");

        var lot = await _db.InventoryLots.FirstOrDefaultAsync(item =>
            item.ProductVariantId == variantId && item.LotNumber == normalizedLotNumber);

        if (lot != null)
            throw new InvalidOperationException("This lot already exists.");

        lot = new InventoryLot
        {
            ProductVariantId = variantId,
            LotNumber = normalizedLotNumber,
            ManufacturingDate = manufacturingDate,
            ExpiryDate = expiryDate,
            ReceivedAt = DateTime.UtcNow
        };

        _db.InventoryLots.Add(lot);
        await _db.SaveChangesAsync();
        return lot;
    }

    public async Task<object> GetLotByIdAsync(int id)
    {
        return await _db.InventoryLots
            .AsNoTracking()
            .Where(lot => lot.Id == id)
            .Select(lot => new
            {
                lot.Id,
                VariantId = lot.ProductVariantId,
                lot.ProductVariant.SKU,
                ProductName = lot.ProductVariant.Product.Name,
                lot.LotNumber,
                lot.ManufacturingDate,
                lot.ExpiryDate,
                lot.ReceivedAt,
                TotalQuantity = lot.BranchStocks.Sum(stock => stock.Quantity)
            })
            .FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException("Inventory lot not found.");
    }

    private async Task<InventoryLot> GetOrCreateDefaultLotAsync(int variantId)
    {
        var lot = await _db.InventoryLots
            .OrderBy(item => item.ReceivedAt)
            .ThenBy(item => item.Id)
            .FirstOrDefaultAsync(item => item.ProductVariantId == variantId);

        if (lot != null)
            return lot;

        var sku = await _db.ProductVariants
            .Where(variant => variant.Id == variantId)
            .Select(variant => variant.SKU)
            .FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException("Variant not found.");

        lot = new InventoryLot
        {
            ProductVariantId = variantId,
            LotNumber = BuildDefaultLotNumber(sku),
            ReceivedAt = DateTime.UtcNow
        };

        _db.InventoryLots.Add(lot);
        return lot;
    }

    public async Task AddToLotAsync(int branchId, InventoryLot lot, int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero.");

        if (lot.Id == 0)
            await _db.SaveChangesAsync();

        var stock = await _db.BranchLotStocks.FirstOrDefaultAsync(item =>
            item.BranchId == branchId && item.InventoryLotId == lot.Id);

        if (stock == null)
        {
            stock = new BranchLotStock
            {
                BranchId = branchId,
                InventoryLotId = lot.Id,
                Quantity = 0
            };
            _db.BranchLotStocks.Add(stock);
        }

        stock.Quantity += quantity;
        stock.LastUpdated = DateTime.UtcNow;
        await TouchLocationAsync(branchId, lot.ProductVariantId);
    }

    public async Task<Dictionary<int, int>> DeductAvailableQuantityAsync(
        int branchId,
        int variantId,
        int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var stocks = await _db.BranchLotStocks
            .Include(stock => stock.InventoryLot)
            .Where(stock =>
                stock.BranchId == branchId &&
                stock.InventoryLot.ProductVariantId == variantId &&
                stock.Quantity > 0 &&
                (!stock.InventoryLot.ExpiryDate.HasValue || stock.InventoryLot.ExpiryDate >= today))
            .OrderBy(stock => !stock.InventoryLot.ExpiryDate.HasValue)
            .ThenBy(stock => stock.InventoryLot.ExpiryDate)
            .ThenBy(stock => stock.InventoryLot.ReceivedAt)
            .ThenBy(stock => stock.InventoryLotId)
            .ToListAsync();

        if (stocks.Sum(stock => stock.Quantity) < quantity)
            throw new ArgumentException($"Insufficient stock for variant {variantId}.");

        var remaining = quantity;
        var allocations = new Dictionary<int, int>();

        foreach (var stock in stocks)
        {
            if (remaining == 0)
                break;

            var deducted = Math.Min(stock.Quantity, remaining);
            stock.Quantity -= deducted;
            stock.LastUpdated = DateTime.UtcNow;
            allocations[stock.InventoryLotId] = deducted;
            remaining -= deducted;
        }

        await TouchLocationAsync(branchId, variantId);
        return allocations;
    }

    public async Task<(InventoryLot Lot, int QuantityBefore, int QuantityAfter)>
        ApplyQuantityChangeAsync(
        int variantId,
        int branchId,
        int inventoryLotId,
        int quantityChange)
    {
        var lot = await _db.InventoryLots
            .Include(item => item.ProductVariant)
            .FirstOrDefaultAsync(item => item.Id == inventoryLotId)
            ?? throw new KeyNotFoundException("Inventory lot not found.");

        if (lot.ProductVariantId != variantId)
            throw new ArgumentException(
                $"Lot {inventoryLotId} does not belong to variant {variantId}.");

        var stock = await _db.BranchLotStocks.FirstOrDefaultAsync(item =>
            item.BranchId == branchId && item.InventoryLotId == inventoryLotId);

        var quantityBefore = stock?.Quantity ?? 0;
        var quantityAfter = quantityBefore + quantityChange;
        if (quantityAfter < 0)
            throw new ArgumentException("This adjustment would reduce the lot quantity below zero.");

        if (stock == null && quantityChange > 0)
        {
            stock = new BranchLotStock
            {
                BranchId = branchId,
                InventoryLotId = inventoryLotId
            };
            _db.BranchLotStocks.Add(stock);
        }

        if (stock != null && quantityChange != 0)
        {
            stock.Quantity = quantityAfter;
            stock.LastUpdated = DateTime.UtcNow;
            await TouchLocationAsync(branchId, lot.ProductVariantId);
        }

        return (lot, quantityBefore, quantityAfter);
    }

    public async Task<InventoryLot> DeductFromLotAsync(
        int branchId,
        int inventoryLotId,
        int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero.");

        var stock = await _db.BranchLotStocks
            .Include(item => item.InventoryLot)
            .FirstOrDefaultAsync(item =>
                item.BranchId == branchId &&
                item.InventoryLotId == inventoryLotId)
            ?? throw new KeyNotFoundException(
                $"Lot {inventoryLotId} has no stock at branch {branchId}.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (stock.InventoryLot.ExpiryDate.HasValue &&
            stock.InventoryLot.ExpiryDate.Value < today)
            throw new ArgumentException($"Lot {inventoryLotId} is expired.");

        if (stock.Quantity < quantity)
            throw new ArgumentException(
                $"Lot {inventoryLotId} only has {stock.Quantity} units available.");

        stock.Quantity -= quantity;
        stock.LastUpdated = DateTime.UtcNow;
        await TouchLocationAsync(branchId, stock.InventoryLot.ProductVariantId);
        return stock.InventoryLot;
    }

    public async Task<Dictionary<int, int>> ReturnSoldQuantityAsync(
        int orderId,
        int variantId,
        int branchId,
        int quantity)
    {
        var orderItem = await _db.OrderItems
            .Include(item => item.Lots)
                .ThenInclude(item => item.InventoryLot)
            .FirstOrDefaultAsync(item =>
                item.OrderId == orderId && item.ProductVariantId == variantId)
            ?? throw new KeyNotFoundException("Order item not found.");

        var allocations = new Dictionary<int, int>();
        var remaining = quantity;

        foreach (var soldLot in orderItem.Lots
                     .Where(item => item.ReturnedQuantity < item.Quantity)
                     .OrderBy(item => item.InventoryLot.ExpiryDate)
                     .ThenBy(item => item.InventoryLotId))
        {
            if (remaining == 0)
                break;

            var returned = Math.Min(soldLot.Quantity - soldLot.ReturnedQuantity, remaining);
            await AddToLotAsync(branchId, soldLot.InventoryLot, returned);
            soldLot.ReturnedQuantity += returned;
            allocations[soldLot.InventoryLotId] = returned;
            remaining -= returned;
        }

        if (remaining > 0)
        {
            var defaultLot = await GetOrCreateDefaultLotAsync(variantId);
            await AddToLotAsync(branchId, defaultLot, remaining);
            allocations[defaultLot.Id] = remaining;
        }

        return allocations;
    }

    public async Task<object> GetPagedLotsAsync(
        string? search,
        int? productId,
        int? variantId,
        int? branchId,
        bool includeEmpty,
        bool includeExpired,
        int page)
    {
        page = Math.Max(page, 1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var query = _db.InventoryLots.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().ToLower()}%";
            query = query.Where(lot =>
                EF.Functions.Like(lot.LotNumber.ToLower(), pattern) ||
                EF.Functions.Like(lot.ProductVariant.SKU.ToLower(), pattern) ||
                EF.Functions.Like(lot.ProductVariant.Product.Name.ToLower(), pattern));
        }

        if (productId.HasValue)
            query = query.Where(lot => lot.ProductVariant.ProductId == productId.Value);
        if (variantId.HasValue)
            query = query.Where(lot => lot.ProductVariantId == variantId);
        if (!includeExpired)
            query = query.Where(lot => !lot.ExpiryDate.HasValue || lot.ExpiryDate >= today);
        if (!includeEmpty)
            query = query.Where(lot => lot.BranchStocks.Any(stock =>
                (!branchId.HasValue || stock.BranchId == branchId.Value) &&
                stock.Quantity > 0));

        var totalCount = await query.CountAsync();
        var lots = await query
            .OrderByDescending(lot => lot.ReceivedAt)
            .ThenByDescending(lot => lot.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(lot => new
            {
                lot.Id,
                lot.ProductVariantId,
                SKU = lot.ProductVariant.SKU,
                ProductName = lot.ProductVariant.Product.Name,
                lot.LotNumber,
                lot.ManufacturingDate,
                lot.ExpiryDate,
                lot.ReceivedAt,
                TotalQuantity = lot.BranchStocks
                    .Where(stock => !branchId.HasValue || stock.BranchId == branchId.Value)
                    .Sum(stock => stock.Quantity),
                Branch = lot.BranchStocks
                    .Where(stock =>
                        (!branchId.HasValue || stock.BranchId == branchId.Value) &&
                        (includeEmpty || stock.Quantity > 0))
                    .OrderBy(stock => stock.Branch.Name)
                    .Select(stock => new
                    {
                        stock.BranchId,
                        BranchName = stock.Branch.Name,
                        stock.Quantity,
                        stock.LastUpdated
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
            lots
        };
    }

    private async Task TouchLocationAsync(int branchId, int variantId)
    {
        var location = await _db.LocationStocks.FirstOrDefaultAsync(stock =>
            stock.BranchId == branchId && stock.ProductVariantId == variantId);

        if (location == null)
        {
            location = new LocationStock
            {
                BranchId = branchId,
                ProductVariantId = variantId,
                ReorderLevel = 0
            };
            _db.LocationStocks.Add(location);
        }

        location.LastUpdated = DateTime.UtcNow;
    }

    public static string BuildDefaultLotNumber(string sku) =>
        $"{sku.Trim().ToUpperInvariant()}-{DateTime.UtcNow:ddMMyyyy}";
}
