namespace Stockify.Services;

public class StockTransferService
{
    private const int PageSize = 20;
    private readonly AppDbContext _db;
    private readonly InventoryLotService _inventory;
    private readonly LookupService _lookups;

    public StockTransferService(AppDbContext db, InventoryLotService inventory, LookupService lookups)
    {
        _db = db;
        _inventory = inventory;
        _lookups = lookups;
    }

    private async Task<string> StatusCodeAsync(int statusId) =>
        (await _lookups.FindAsync<StockTransferStatus>(statusId))?.Code ?? string.Empty;

    private Task<int> StatusIdAsync(string code) => _lookups.IdAsync<StockTransferStatus>(code);

    private Task<int> MovementTypeIdAsync(string code) => _lookups.IdAsync<MovementType>(code);

    public async Task<object> CreateTransferAsync(
        CreateStockTransferRequest dto,
        int userId)
    {
        // Validate destination branch exists
        var destBranch = await _db.Branches.FindAsync(dto.DestinationBranchId);
        if (destBranch == null || destBranch.IsDeleted)
            throw new KeyNotFoundException("Destination branch not found");

        if (dto.SourceBranchId.HasValue)
        {
            if (dto.SourceBranchId.Value == dto.DestinationBranchId)
                throw new ArgumentException("Source and destination branches cannot be the same.");

            var sourceBranch = await _db.Branches.FindAsync(dto.SourceBranchId.Value);
            if (sourceBranch == null || sourceBranch.IsDeleted)
                throw new KeyNotFoundException("Source branch not found");
        }

        // Validate all variants exist
        var variantIds = dto.Items.Select(i => i.ProductVariantId).Distinct().ToList();
        var existingVariants = await _db.ProductVariants
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => v.Id)
            .ToListAsync();

        var missingVariants = variantIds.Except(existingVariants).ToList();
        if (missingVariants.Any())
            throw new ArgumentException($"Variants not found: {string.Join(", ", missingVariants)}");

        var transfer = new StockTransfer
        {
            SourceBranchId = dto.SourceBranchId,
            DestinationBranchId = dto.DestinationBranchId,
            Notes = dto.Notes,
            StatusId = await StatusIdAsync(StockTransferStatusCodes.Draft),
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            Items = dto.Items.Select(i => new StockTransferItem
            {
                ProductVariantId = i.ProductVariantId,
                QuantityRequested = i.Quantity,
                QuantitySent = 0,
                QuantityReceived = 0
            }).ToList()
        };

        _db.StockTransfers.Add(transfer);
        await _db.SaveChangesAsync();

        // Load navigation properties for response
        if (transfer.SourceBranchId.HasValue)
            await _db.Entry(transfer).Reference(t => t.SourceBranch).LoadAsync();
        await _db.Entry(transfer).Reference(t => t.DestinationBranch).LoadAsync();
        await _db.Entry(transfer).Reference(t => t.CreatedBy).LoadAsync();
        await _db.Entry(transfer).Collection(t => t.Items).LoadAsync();

        var createdVariantIds = transfer.Items.Select(i => i.ProductVariantId).Distinct().ToList();
        var createdVariantInfo = await _db.ProductVariants
            .Where(v => createdVariantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.SKU, v.Barcode })
            .ToDictionaryAsync(v => v.Id);

        return new
        {
            transfer.Id,
            transfer.SourceBranchId,
            SourceBranchName = transfer.SourceBranch?.Name,
            transfer.DestinationBranchId,
            DestinationBranchName = transfer.DestinationBranch.Name,
            transfer.Status,
            transfer.Notes,
            transfer.CreatedAt,
            CreatedBy = transfer.CreatedBy.Username,
            Items = transfer.Items.Select(i => new
            {
                i.Id,
                i.ProductVariantId,
                SKU = createdVariantInfo.TryGetValue(i.ProductVariantId, out var v) ? v.SKU : null,
                Barcode = createdVariantInfo.TryGetValue(i.ProductVariantId, out var v2) ? v2.Barcode : null,
                i.QuantityRequested,
                i.QuantitySent,
                i.QuantityReceived
            })
        };
    }

    public async Task<object> DispatchTransferAsync(
        int id,
        DispatchStockTransferRequest dto)
    {
        var transfer = await _db.StockTransfers
            .Include(item => item.Items)
            .Include(item => item.DestinationBranch)
            .FirstOrDefaultAsync(item => item.Id == id)
            ?? throw new KeyNotFoundException("Transfer not found.");

        if (await StatusCodeAsync(transfer.StatusId) != StockTransferStatusCodes.Draft)
            throw new ArgumentException("Only draft transfers can be dispatched.");
        if (dto.Lots.Count == 0)
            throw new ArgumentException("At least one lot is required.");

        var sourceBranchId = transfer.SourceBranchId ?? dto.SourceBranchId;
        var sourceBranch = await _db.Branches.FindAsync(sourceBranchId)
            ?? throw new KeyNotFoundException("Source branch not found.");
        if (sourceBranch.IsDeleted)
            throw new KeyNotFoundException("Source branch not found.");
        if (sourceBranchId == transfer.DestinationBranchId)
            throw new ArgumentException("Source and destination branches cannot be the same.");

        var allocations = dto.Lots
            .GroupBy(item => new
            {
                item.StockTransferItemId,
                item.InventoryLotId
            })
            .Select(group => new
            {
                group.Key.StockTransferItemId,
                group.Key.InventoryLotId,
                Quantity = group.Sum(item => item.Quantity)
            })
            .ToList();

        if (allocations.Any(item => item.Quantity <= 0))
            throw new ArgumentException("Every lot quantity must be greater than zero.");

        foreach (var item in transfer.Items)
        {
            var quantity = allocations
                .Where(allocation => allocation.StockTransferItemId == item.Id)
                .Sum(allocation => allocation.Quantity);
            if (quantity == 0)
                throw new ArgumentException($"Lots are required for transfer item {item.Id}.");
            if (quantity > item.QuantityRequested)
                throw new ArgumentException(
                    $"Transfer item {item.Id} cannot exceed its requested quantity.");
        }

        var transferItemIds = transfer.Items.Select(item => item.Id).ToHashSet();
        if (allocations.Any(item => !transferItemIds.Contains(item.StockTransferItemId)))
            throw new ArgumentException("A lot references an item outside this transfer.");

        await using var transaction = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        transfer.SourceBranchId = sourceBranchId;
        transfer.SourceBranch = sourceBranch;

        foreach (var allocation in allocations)
        {
            var item = transfer.Items.Single(item =>
                item.Id == allocation.StockTransferItemId);
            var lot = await _inventory.DeductFromLotAsync(
                sourceBranchId,
                allocation.InventoryLotId,
                allocation.Quantity);

            if (lot.ProductVariantId != item.ProductVariantId)
                throw new ArgumentException(
                    $"Lot {lot.Id} does not belong to transfer item {item.Id}.");

            item.QuantitySent += allocation.Quantity;
            _db.StockTransferItemLots.Add(new StockTransferItemLot
            {
                StockTransferItemId = item.Id,
                InventoryLotId = lot.Id,
                QuantitySent = allocation.Quantity
            });
            _db.StockMovements.Add(new StockMovement
            {
                ProductVariantId = item.ProductVariantId,
                InventoryLotId = lot.Id,
                BranchId = sourceBranchId,
                ToBranchId = transfer.DestinationBranchId,
                MovementTypeId = await MovementTypeIdAsync(MovementTypeCodes.TransferOut),
                Quantity = -allocation.Quantity,
                Reason = $"TR #{id} to {transfer.DestinationBranch.Name}",
                CreatedAt = DateTime.UtcNow
            });
        }

        transfer.StatusId = await StatusIdAsync(StockTransferStatusCodes.Sent);
        transfer.SentAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(dto.Notes))
            transfer.Notes = $"{transfer.Notes}\n[Sent {DateTime.UtcNow:yyyy-MM-dd HH:mm}] {dto.Notes.Trim()}".Trim();

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return new
        {
            transfer.Id,
            transfer.SentAt,
            SourceBranch = sourceBranch.Name,
            DestinationBranch = transfer.DestinationBranch.Name,
            Lots = allocations
        };
    }

    public async Task<object> RecordTransferReceiptAsync(
        int id,
        ReceiveStockTransferRequest dto)
    {
        var transfer = await _db.StockTransfers
            .Include(t => t.Items)
                .ThenInclude(item => item.Lots)
                .ThenInclude(item => item.InventoryLot)
            .Include(t => t.SourceBranch)
            .Include(t => t.DestinationBranch)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (transfer == null)
            throw new KeyNotFoundException("Transfer not found");

        var status = await StatusCodeAsync(transfer.StatusId);
        if (status == StockTransferStatusCodes.Draft)
            throw new ArgumentException("Draft transfers cannot be received. Send the transfer first.");

        if (status == StockTransferStatusCodes.Completed)
            throw new ArgumentException("Transfer is already completed");

        if (status == StockTransferStatusCodes.Cancelled)
            throw new ArgumentException("Cancelled transfers cannot be received");

        await using var transaction = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        if (dto.Lots.Count == 0 && !dto.Complete)
            throw new ArgumentException("At least one received lot is required.");

        var receivedLots = dto.Lots
            .GroupBy(item => new
            {
                item.StockTransferItemId,
                item.InventoryLotId
            })
            .Select(group => new
            {
                group.Key.StockTransferItemId,
                group.Key.InventoryLotId,
                QuantityReceived = group.Max(item => item.QuantityReceived)
            })
            .ToList();

        // Quantities are cumulative, so retries do not add the same receipt twice.
        foreach (var receivedLot in receivedLots)
        {
            var item = transfer.Items.FirstOrDefault(i =>
                i.Id == receivedLot.StockTransferItemId);
            if (item == null)
                throw new ArgumentException(
                    $"Transfer item {receivedLot.StockTransferItemId} not found.");

            var lotAllocation = item.Lots.FirstOrDefault(lot =>
                lot.InventoryLotId == receivedLot.InventoryLotId)
                ?? throw new ArgumentException(
                    $"Lot {receivedLot.InventoryLotId} was not dispatched for transfer item {item.Id}.");
            if (receivedLot.QuantityReceived < lotAllocation.QuantityReceived)
                throw new ArgumentException("Received lot quantity cannot be reduced.");
            if (receivedLot.QuantityReceived > lotAllocation.QuantitySent)
                throw new ArgumentException(
                    "Received lot quantity cannot exceed its dispatched quantity.");

            var quantityToReceive =
                receivedLot.QuantityReceived - lotAllocation.QuantityReceived;
            if (quantityToReceive > 0)
            {
                await _inventory.AddToLotAsync(
                    transfer.DestinationBranchId,
                    lotAllocation.InventoryLot,
                    quantityToReceive);
                _db.StockMovements.Add(new StockMovement
                {
                    ProductVariantId = item.ProductVariantId,
                    InventoryLotId = lotAllocation.InventoryLotId,
                    BranchId = transfer.DestinationBranchId,
                    MovementTypeId = await MovementTypeIdAsync(MovementTypeCodes.TransferIn),
                    Quantity = quantityToReceive,
                    Reason = $"TR #{id} from {transfer.SourceBranch!.Name}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            lotAllocation.QuantityReceived = receivedLot.QuantityReceived;
        }

        foreach (var item in transfer.Items)
            item.QuantityReceived = item.Lots.Sum(lot => lot.QuantityReceived);

        var allReceived = transfer.Items.All(i =>
            i.QuantitySent > 0 && i.QuantityReceived == i.QuantitySent);
        var isCompleted = allReceived || dto.Complete;
        transfer.StatusId = await StatusIdAsync(
            isCompleted ? StockTransferStatusCodes.Completed : StockTransferStatusCodes.PartiallyReceived);

        if (isCompleted)
            transfer.ReceivedAt = DateTime.UtcNow;

        var hasDiscrepancy = isCompleted &&
            transfer.Items.Any(i => i.QuantitySent != i.QuantityReceived);
        transfer.HasDiscrepancy = hasDiscrepancy;
        transfer.IsResolved = !hasDiscrepancy;

        if (!string.IsNullOrEmpty(dto.Notes))
        {
            transfer.Notes = (transfer.Notes ?? "") + $"\n[Received {DateTime.UtcNow:yyyy-MM-dd HH:mm}] {dto.Notes}";
        }

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return new
        {
            transfer.Id,
            Status = isCompleted ? "Completed" : "PartiallyReceived",
            transfer.ReceivedAt,
            transfer.HasDiscrepancy,
            SourceBranch = transfer.SourceBranch!.Name,
            DestinationBranch = transfer.DestinationBranch.Name,
            ItemsReceived = transfer.Items.Count(i => i.QuantityReceived > 0),
            TotalItems = transfer.Items.Count
        };
    }

    public async Task CancelTransferAsync(int id, string? reason)
    {
        var transfer = await _db.StockTransfers
            .Include(t => t.Items)
                .ThenInclude(item => item.Lots)
                .ThenInclude(item => item.InventoryLot)
            .Include(t => t.SourceBranch)
            .Include(t => t.DestinationBranch)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (transfer == null)
            throw new KeyNotFoundException("Transfer not found");

        var status = await StatusCodeAsync(transfer.StatusId);
        if (status == StockTransferStatusCodes.Completed)
            throw new ArgumentException("Completed transfers cannot be cancelled");

        if (status == StockTransferStatusCodes.Cancelled)
            throw new ArgumentException("Transfer is already cancelled");

        using var transaction = await _db.Database.BeginTransactionAsync();

        // If transfer was sent, return stock to source
        if (status is StockTransferStatusCodes.Sent or StockTransferStatusCodes.PartiallyReceived)
        {
            foreach (var item in transfer.Items)
            {
                foreach (var lotAllocation in item.Lots.Where(lot =>
                             lot.QuantitySent > lot.QuantityReceived))
                {
                    var quantityToReturn =
                        lotAllocation.QuantitySent - lotAllocation.QuantityReceived;

                    await _inventory.AddToLotAsync(
                        transfer.SourceBranchId!.Value,
                        lotAllocation.InventoryLot,
                        quantityToReturn);

                    _db.StockMovements.Add(new StockMovement
                    {
                        ProductVariantId = item.ProductVariantId,
                        InventoryLotId = lotAllocation.InventoryLotId,
                        BranchId = transfer.SourceBranchId!.Value,
                        MovementTypeId = await MovementTypeIdAsync(MovementTypeCodes.TransferCancelled),
                        Quantity = quantityToReturn,
                        Reason = $"TR #{id} cancelled - {reason ?? "No reason provided"}",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
        }

        // Update transfer status
        transfer.StatusId = await StatusIdAsync(StockTransferStatusCodes.Cancelled);
        transfer.Notes = (transfer.Notes ?? "") + $"\n[Cancelled {DateTime.UtcNow:yyyy-MM-dd HH:mm}] {reason ?? "No reason provided"}";

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<object> GetTransferByIdAsync(int id)
    {
        var transfer = await _db.StockTransfers
            .Include(t => t.SourceBranch)
            .Include(t => t.DestinationBranch)
            .Include(t => t.Status)
            .Include(t => t.CreatedBy)
            .Include(t => t.Items)
                .ThenInclude(i => i.ProductVariant)
                .ThenInclude(pv => pv.Product)
            .Include(t => t.Items)
                .ThenInclude(i => i.ProductVariant)
                .ThenInclude(pv => pv.AttributeValues)
                .ThenInclude(value => value.ProductAttribute)
            .Include(t => t.Items)
                .ThenInclude(item => item.Lots)
                .ThenInclude(item => item.InventoryLot)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (transfer == null)
            throw new KeyNotFoundException("Transfer not found");

        return new
        {
            transfer.Id,
            transfer.SourceBranchId,
            SourceBranch = transfer.SourceBranch?.Name,
            transfer.DestinationBranchId,
            DestinationBranch = transfer.DestinationBranch.Name,
            transfer.StatusId,
            StatusCode = transfer.Status.Code,
            Status = transfer.Status.Name,
            transfer.Notes,
            transfer.CreatedAt,
            transfer.SentAt,
            transfer.ReceivedAt,
            CreatedBy = new
            {
                transfer.CreatedBy.Id,
                transfer.CreatedBy.Username,
            },
            Items = transfer.Items.Select(i => new
            {
                i.Id,
                i.ProductVariantId,
                ProductName = i.ProductVariant.Product.Name,
                VariantSKU = i.ProductVariant.SKU,
                Barcode = i.ProductVariant.Barcode,
                Attributes = i.ProductVariant.AttributeValues
                    .OrderBy(value => value.ProductAttribute.Name)
                    .Select(value => new
                    {
                        Name = value.ProductAttribute.Name,
                        value.Value
                    }),
                i.QuantityRequested,
                i.QuantitySent,
                i.QuantityReceived,
                Lots = i.Lots.Select(lot => new
                {
                    lot.InventoryLotId,
                    lot.InventoryLot.LotNumber,
                    lot.InventoryLot.ManufacturingDate,
                    lot.InventoryLot.ExpiryDate,
                    lot.QuantitySent,
                    lot.QuantityReceived
                }),
                Discrepancy = i.QuantitySent - i.QuantityReceived,
                i.ReceivingNotes
            }).ToList()
        };
    }

    public async Task<object> GetPagedTransfersAsync(
        string? search,
        int? branchId,
        int? sourceBranchId,
        int? destinationBranchId,
        int? statusId,
        DateTime? startDate,
        DateTime? endDate,
        int page)
    {
        page = Math.Max(page, 1);
        var query = _db.StockTransfers
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().ToLower()}%";
            query = query.Where(transfer =>
                (transfer.Notes != null &&
                 EF.Functions.Like(transfer.Notes.ToLower(), pattern)) ||
                EF.Functions.Like(transfer.CreatedBy.Username.ToLower(), pattern) ||
                (transfer.SourceBranch != null &&
                 EF.Functions.Like(transfer.SourceBranch.Name.ToLower(), pattern)) ||
                EF.Functions.Like(transfer.DestinationBranch.Name.ToLower(), pattern) ||
                transfer.Items.Any(item =>
                    EF.Functions.Like(item.ProductVariant.SKU.ToLower(), pattern) ||
                    item.Lots.Any(lot =>
                        lot.InventoryLot.LotNumber != null &&
                        EF.Functions.Like(lot.InventoryLot.LotNumber.ToLower(), pattern))));
        }

        if (branchId.HasValue)
            query = query.Where(t => t.SourceBranchId == branchId.Value ||
                                    t.DestinationBranchId == branchId.Value);
        if (sourceBranchId.HasValue)
            query = query.Where(t => t.SourceBranchId == sourceBranchId.Value);
        if (destinationBranchId.HasValue)
            query = query.Where(t =>
                t.DestinationBranchId == destinationBranchId.Value);
        if (statusId.HasValue)
            query = query.Where(t => t.StatusId == statusId.Value);
        if (startDate.HasValue)
            query = query.Where(t => t.CreatedAt >= startDate.Value.Date);
        if (endDate.HasValue)
            query = query.Where(t => t.CreatedAt < endDate.Value.Date.AddDays(1));

        var totalCount = await query.CountAsync();
        var transfers = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(t => new
            {
                t.Id,
                t.SourceBranchId,
                SourceBranch = t.SourceBranch != null ? t.SourceBranch.Name : null,
                t.DestinationBranchId,
                DestinationBranch = t.DestinationBranch.Name,
                t.StatusId,
                StatusCode = t.Status.Code,
                Status = t.Status.Name,
                t.CreatedAt,
                t.SentAt,
                t.ReceivedAt,
                t.Notes,
                CreatedBy = t.CreatedBy.Username,
                ItemCount = t.Items.Count,
                t.HasDiscrepancy,
                t.IsResolved,
                t.ResolutionNote,
                Items = t.Items.Select(i => new
                {
                    i.Id,
                    i.ProductVariantId,
                    SKU = i.ProductVariant.SKU,
                    Barcode = i.ProductVariant.Barcode,
                    i.QuantityRequested,
                    i.QuantitySent,
                    i.QuantityReceived,
                    i.ReceivingNotes,
                    Lots = i.Lots
                        .OrderBy(lot => lot.InventoryLotId)
                        .Select(lot => new
                        {
                            lot.InventoryLotId,
                            lot.InventoryLot.LotNumber,
                            lot.InventoryLot.ManufacturingDate,
                            lot.InventoryLot.ExpiryDate,
                            lot.QuantitySent,
                            lot.QuantityReceived
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
            transfers
        };
    }

    public async Task<object> GetTransferStatisticsAsync(int? branchId)
    {
        var query = _db.StockTransfers
            .Include(t => t.Status)
            .AsQueryable();

        if (branchId.HasValue)
        {
            query = query.Where(t => t.SourceBranchId == branchId.Value ||
                                    t.DestinationBranchId == branchId.Value);
        }

        var stats = await query.GroupBy(t => new { t.Status.Code, t.Status.Name })
            .Select(g => new
            {
                StatusCode = g.Key.Code,
                Status = g.Key.Name,
                Count = g.Count()
            })
            .ToListAsync();

        var totalTransfers = await query.CountAsync();
        var thisMonthTransfers = await query
            .Where(t => t.CreatedAt >= DateTime.UtcNow.AddMonths(-1))
            .CountAsync();

        return new
        {
            totalTransfers,
            thisMonthTransfers,
            byStatus = stats
        };
    }

    public async Task<object> ResolveTransferDiscrepancyAsync(
        int id,
        ResolveStockTransferDiscrepancyRequest dto)
    {
        var transfer = await _db.StockTransfers
            .Include(t => t.Status)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (transfer == null)
            throw new KeyNotFoundException("Transfer not found");

        if (transfer.Status.Code != StockTransferStatusCodes.Completed)
            throw new ArgumentException("Only completed transfers can have discrepancies resolved");

        if (!transfer.HasDiscrepancy)
            throw new ArgumentException("This transfer has no discrepancy");

        if (transfer.IsResolved)
            throw new ArgumentException("This discrepancy has already been resolved");

        transfer.IsResolved = true;
        transfer.ResolutionNote = dto.Note;
        transfer.HasDiscrepancy = false;

        await _db.SaveChangesAsync();

        return new
        {
            transfer.Id,
            Status = transfer.Status.Name,
            transfer.HasDiscrepancy,
            transfer.IsResolved,
            transfer.ResolutionNote
        };
    }
}
