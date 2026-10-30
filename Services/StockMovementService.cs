namespace Stockify.Services;

public class StockMovementService
{
    private const int PageSize = 20;
    private readonly AppDbContext _db;

    public StockMovementService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<object> GetAllAsync(
        string? search,
        int? movementTypeId,
        int? branchId,
        int? variantId,
        int? inventoryLotId,
        DateTime? startDate,
        DateTime? endDate,
        int page)
    {
        page = Math.Max(page, 1);
        var query = _db.StockMovements.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().ToLower()}%";
            query = query.Where(movement =>
                EF.Functions.Like(movement.ProductVariant.Product.Name.ToLower(), pattern) ||
                EF.Functions.Like(movement.ProductVariant.SKU.ToLower(), pattern) ||
                (movement.Reason != null && EF.Functions.Like(movement.Reason.ToLower(), pattern)) ||
                (movement.InventoryLot != null &&
                 movement.InventoryLot.LotNumber != null &&
                 EF.Functions.Like(movement.InventoryLot.LotNumber.ToLower(), pattern)));
        }

        if (movementTypeId.HasValue)
            query = query.Where(sm => sm.MovementTypeId == movementTypeId.Value);
        if (branchId.HasValue)
            query = query.Where(movement => movement.BranchId == branchId.Value);
        if (variantId.HasValue)
            query = query.Where(movement => movement.ProductVariantId == variantId.Value);
        if (inventoryLotId.HasValue)
            query = query.Where(movement => movement.InventoryLotId == inventoryLotId.Value);

        if (startDate.HasValue)
        {
            query = query.Where(sm => sm.CreatedAt >= startDate.Value.Date);
        }

        if (endDate.HasValue)
        {
            var endOfDay = endDate.Value.Date.AddDays(1);
            query = query.Where(sm => sm.CreatedAt < endOfDay);
        }

        var totalCount = await query.CountAsync();

        var rows = await query
            .OrderByDescending(sm => sm.CreatedAt)
            .ThenByDescending(sm => sm.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(sm => new
            {
                ProductName = sm.ProductVariant.Product.Name,
                SKU = sm.ProductVariant.SKU,
                sm.MovementTypeId,
                MovementTypeCode = sm.MovementType.Code,
                MovementType = sm.MovementType.Name,
                Quantity = sm.Quantity,
                CreatedAt = sm.CreatedAt,
                Reason = sm.Reason,
                BranchId = sm.BranchId,
                BranchName = sm.Branch.Name,
                sm.InventoryLotId,
                LotNumber = sm.InventoryLot != null ? sm.InventoryLot.LotNumber : null,
                ExpiryDate = sm.InventoryLot != null ? sm.InventoryLot.ExpiryDate : null
            })
            .ToListAsync();

        var result = new
        {
            totalCount,
            page,
            perPage = PageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)PageSize),
            movements = rows
        };

        return result;
    }

    public async Task<IEnumerable<object>> GetMovementTypesAsync()
    {
        return await _db.MovementTypes
            .AsNoTracking()
            .OrderBy(t => t.SortOrder)
            .Select(t => new { t.Id, t.Code, t.Name, t.Tone })
            .ToListAsync();
    }
}
