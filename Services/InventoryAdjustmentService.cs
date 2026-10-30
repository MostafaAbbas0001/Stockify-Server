namespace Stockify.Services;

public class InventoryAdjustmentService
{
    private const int PageSize = 20;

    private readonly AppDbContext _db;
    private readonly InventoryLotService _inventory;
    private readonly LookupService _lookups;

    public InventoryAdjustmentService(
        AppDbContext db,
        InventoryLotService inventory,
        LookupService lookups)
    {
        _db = db;
        _inventory = inventory;
        _lookups = lookups;
    }

    public async Task<object> CreateAdjustmentAsync(
        CreateInventoryAdjustmentRequest request)
    {
        ValidateRequest(
            request.VariantId,
            request.BranchId,
            request.InventoryLotId,
            request.Quantity,
            request.Reason);
        await EnsureBranchExistsAsync(request.BranchId);

        await using var transaction = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        var stockChange = await _inventory.ApplyQuantityChangeAsync(
            request.VariantId,
            request.BranchId,
            request.InventoryLotId,
            request.Quantity);

        var adjustment = new InventoryAdjustment
        {
            BranchId = request.BranchId,
            InventoryLotId = request.InventoryLotId,
            QuantityBefore = stockChange.QuantityBefore,
            QuantityAfter = stockChange.QuantityAfter,
            DeltaQuantity = request.Quantity,
            Reason = request.Reason.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _db.InventoryAdjustments.Add(adjustment);
        _db.StockMovements.Add(await CreateMovementAsync(
            request.VariantId,
            request.BranchId,
            request.InventoryLotId,
            request.Quantity,
            request.Reason));

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetAdjustmentByIdAsync(adjustment.Id);
    }

    public async Task<object> UpdateAdjustmentAsync(
        int id,
        UpdateInventoryAdjustmentRequest request)
    {
        ValidateRequest(
            request.VariantId,
            request.BranchId,
            request.InventoryLotId,
            request.Quantity,
            request.Reason);
        await EnsureBranchExistsAsync(request.BranchId);

        await using var transaction = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        var adjustment = await _db.InventoryAdjustments
            .Include(item => item.InventoryLot)
            .FirstOrDefaultAsync(item => item.Id == id)
            ?? throw new KeyNotFoundException("Inventory adjustment not found.");

        int quantityBefore;
        int quantityAfter;
        var sameStock = adjustment.BranchId == request.BranchId &&
            adjustment.InventoryLotId == request.InventoryLotId;

        if (sameStock)
        {
            var netChange = request.Quantity - adjustment.DeltaQuantity;
            var stockChange = await _inventory.ApplyQuantityChangeAsync(
                request.VariantId,
                request.BranchId,
                request.InventoryLotId,
                netChange);

            quantityAfter = stockChange.QuantityAfter;
            quantityBefore = quantityAfter - request.Quantity;

            if (netChange != 0)
            {
                _db.StockMovements.Add(await CreateMovementAsync(
                    request.VariantId,
                    request.BranchId,
                    request.InventoryLotId,
                    netChange,
                    $"Edited adjustment #{id}: {request.Reason}"));
            }
        }
        else
        {
            var originalVariantId = adjustment.InventoryLot.ProductVariantId;
            var reversalQuantity = -adjustment.DeltaQuantity;
            await _inventory.ApplyQuantityChangeAsync(
                originalVariantId,
                adjustment.BranchId,
                adjustment.InventoryLotId,
                reversalQuantity);

            _db.StockMovements.Add(await CreateMovementAsync(
                originalVariantId,
                adjustment.BranchId,
                adjustment.InventoryLotId,
                reversalQuantity,
                $"Moved adjustment #{id}"));

            var stockChange = await _inventory.ApplyQuantityChangeAsync(
                request.VariantId,
                request.BranchId,
                request.InventoryLotId,
                request.Quantity);

            quantityBefore = stockChange.QuantityBefore;
            quantityAfter = stockChange.QuantityAfter;
            _db.StockMovements.Add(await CreateMovementAsync(
                request.VariantId,
                request.BranchId,
                request.InventoryLotId,
                request.Quantity,
                $"Edited adjustment #{id}: {request.Reason}"));
        }

        adjustment.BranchId = request.BranchId;
        adjustment.InventoryLotId = request.InventoryLotId;
        adjustment.QuantityBefore = quantityBefore;
        adjustment.QuantityAfter = quantityAfter;
        adjustment.DeltaQuantity = request.Quantity;
        adjustment.Reason = request.Reason.Trim();

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetAdjustmentByIdAsync(adjustment.Id);
    }

    public async Task DeleteAdjustmentAsync(int id)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        var adjustment = await _db.InventoryAdjustments
            .Include(item => item.InventoryLot)
            .FirstOrDefaultAsync(item => item.Id == id)
            ?? throw new KeyNotFoundException("Inventory adjustment not found.");

        var reversalQuantity = -adjustment.DeltaQuantity;
        var variantId = adjustment.InventoryLot.ProductVariantId;
        await _inventory.ApplyQuantityChangeAsync(
            variantId,
            adjustment.BranchId,
            adjustment.InventoryLotId,
            reversalQuantity);

        _db.StockMovements.Add(await CreateMovementAsync(
            variantId,
            adjustment.BranchId,
            adjustment.InventoryLotId,
            reversalQuantity,
            $"Deleted adjustment #{id}: {adjustment.Reason}"));
        _db.InventoryAdjustments.Remove(adjustment);

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<object> GetAdjustmentByIdAsync(int id)
    {
        return await _db.InventoryAdjustments
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new
            {
                item.Id,
                item.BranchId,
                BranchName = item.Branch.Name,
                VariantId = item.InventoryLot.ProductVariantId,
                item.InventoryLotId,
                item.InventoryLot.LotNumber,
                SKU = item.InventoryLot.ProductVariant.SKU,
                ProductName = item.InventoryLot.ProductVariant.Product.Name,
                Quantity = item.DeltaQuantity,
                item.QuantityBefore,
                item.QuantityAfter,
                item.Reason,
                item.CreatedAt
            })
            .FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException("Inventory adjustment not found.");
    }

    public async Task<object> GetPagedAdjustmentsAsync(
        string? search,
        int? branchId,
        int? variantId,
        int? inventoryLotId,
        int? minimumQuantity,
        int? maximumQuantity,
        DateTime? startDate,
        DateTime? endDate,
        int page)
    {
        page = Math.Max(page, 1);
        var query = _db.InventoryAdjustments.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().ToLower()}%";
            query = query.Where(item =>
                EF.Functions.Like(item.Reason.ToLower(), pattern) ||
                EF.Functions.Like(item.Branch.Name.ToLower(), pattern) ||
                EF.Functions.Like(
                    item.InventoryLot.ProductVariant.SKU.ToLower(),
                    pattern) ||
                EF.Functions.Like(
                    item.InventoryLot.ProductVariant.Product.Name.ToLower(),
                    pattern) ||
                (item.InventoryLot.LotNumber != null &&
                 EF.Functions.Like(item.InventoryLot.LotNumber.ToLower(), pattern)));
        }

        if (branchId.HasValue)
            query = query.Where(item => item.BranchId == branchId.Value);
        if (variantId.HasValue)
            query = query.Where(item =>
                item.InventoryLot.ProductVariantId == variantId.Value);
        if (inventoryLotId.HasValue)
            query = query.Where(item =>
                item.InventoryLotId == inventoryLotId.Value);
        if (minimumQuantity.HasValue)
            query = query.Where(item =>
                item.DeltaQuantity >= minimumQuantity.Value);
        if (maximumQuantity.HasValue)
            query = query.Where(item =>
                item.DeltaQuantity <= maximumQuantity.Value);
        if (startDate.HasValue)
            query = query.Where(item => item.CreatedAt >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(item =>
                item.CreatedAt < endDate.Value.Date.AddDays(1));

        var totalCount = await query.CountAsync();
        var adjustments = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(item => new
            {
                item.Id,
                item.BranchId,
                BranchName = item.Branch.Name,
                VariantId = item.InventoryLot.ProductVariantId,
                item.InventoryLotId,
                item.InventoryLot.LotNumber,
                SKU = item.InventoryLot.ProductVariant.SKU,
                ProductName = item.InventoryLot.ProductVariant.Product.Name,
                Quantity = item.DeltaQuantity,
                item.QuantityBefore,
                item.QuantityAfter,
                item.Reason,
                item.CreatedAt
            })
            .ToListAsync();

        return new
        {
            totalCount,
            page,
            perPage = PageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)PageSize),
            adjustments
        };
    }

    public async Task UpdateReorderLevelAsync(
        SetReorderLevelRequest request)
    {
        if (request.BranchId <= 0 || request.VariantId <= 0)
            throw new ArgumentException("Branch and variant are required.");
        if (request.ReorderLevel < 0)
            throw new ArgumentException("Reorder level cannot be negative.");

        await EnsureBranchExistsAsync(request.BranchId);
        if (!await _db.ProductVariants.AnyAsync(
                variant => variant.Id == request.VariantId))
            throw new KeyNotFoundException("Variant not found.");

        var location = await _db.LocationStocks.FirstOrDefaultAsync(stock =>
            stock.BranchId == request.BranchId &&
            stock.ProductVariantId == request.VariantId);

        if (location == null)
        {
            location = new LocationStock
            {
                BranchId = request.BranchId,
                ProductVariantId = request.VariantId
            };
            _db.LocationStocks.Add(location);
        }

        location.ReorderLevel = request.ReorderLevel;
        location.LastUpdated = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private async Task EnsureBranchExistsAsync(int branchId)
    {
        if (!await _db.Branches.AnyAsync(branch => branch.Id == branchId))
            throw new KeyNotFoundException("Branch not found.");
    }

    private static void ValidateRequest(
        int variantId,
        int branchId,
        int inventoryLotId,
        int quantity,
        string reason)
    {
        if (variantId <= 0 || branchId <= 0 || inventoryLotId <= 0)
            throw new ArgumentException(
                "Variant, branch, and inventory lot are required.");
        if (quantity == 0)
            throw new ArgumentException("Quantity cannot be zero.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required.");
    }

    private async Task<StockMovement> CreateMovementAsync(
        int variantId,
        int branchId,
        int inventoryLotId,
        int quantity,
        string reason)
    {
        return new StockMovement
        {
            ProductVariantId = variantId,
            InventoryLotId = inventoryLotId,
            BranchId = branchId,
            MovementTypeId = await _lookups.IdAsync<MovementType>(
                quantity > 0 ? MovementTypeCodes.AddManually : MovementTypeCodes.RemoveManually),
            Quantity = quantity,
            Reason = reason.Trim(),
            CreatedAt = DateTime.UtcNow
        };
    }
}
