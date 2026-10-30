namespace Stockify.Services;

public class DashboardService
{
    private readonly AppDbContext _db;

    public DashboardService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<object> GetStockSummaryAsync(DateTimeOffset startUtc, DateTimeOffset endUtcExclusive, int? branchId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var inventoryQuery = _db.LocationStocks.Select(location => new
        {
            VariantId = location.ProductVariantId,
            location.ProductVariant.SKU,
            ProductName = location.ProductVariant.Product.Name,
            location.BranchId,
            BranchName = location.Branch.Name,
            StockQuantity = _db.BranchLotStocks
                .Where(stock =>
                    stock.BranchId == location.BranchId &&
                    stock.InventoryLot.ProductVariantId == location.ProductVariantId &&
                    (!stock.InventoryLot.ExpiryDate.HasValue ||
                     stock.InventoryLot.ExpiryDate >= today))
                .Sum(stock => (int?)stock.Quantity) ?? 0,
            location.ReorderLevel
        });

        if (branchId.HasValue)
            inventoryQuery = inventoryQuery.Where(stock => stock.BranchId == branchId.Value);

        var lowStockVariants = await inventoryQuery
            .Where(stock =>
                stock.StockQuantity > 0 &&
                stock.StockQuantity <= stock.ReorderLevel)
            .ToListAsync();

        var outOfStockVariants = await inventoryQuery
            .Where(stock => stock.StockQuantity == 0)
            .ToListAsync();

        var transferInTransitQuery = _db.StockTransfers
            .Where(st => st.Status.Code == StockTransferStatusCodes.Sent)
            .Where(st => st.SentAt >= startUtc && st.SentAt < endUtcExclusive);

        if (branchId.HasValue)
        {
            transferInTransitQuery = transferInTransitQuery
                .Where(st => st.SourceBranchId == branchId.Value || st.DestinationBranchId == branchId.Value);
        }

        var transferInTransit = await transferInTransitQuery
            .Select(st => new
            {
                st.Id,
                st.StatusId,
                StatusCode = st.Status.Code,
                StatusName = st.Status.Name,
                st.SentAt,
                st.SourceBranchId,
                st.DestinationBranchId,
                SourceBranchName = st.SourceBranch != null
                    ? st.SourceBranch.Name
                    : null,
                DestinationBranchName = st.DestinationBranch.Name
            })
            .ToListAsync();

        var latestTransfersQuery = _db.StockTransfers
            .OrderByDescending(st => st.CreatedAt)
            .Take(3);

        if (branchId.HasValue)
        {
            latestTransfersQuery = latestTransfersQuery
                .Where(st => st.SourceBranchId == branchId.Value || st.DestinationBranchId == branchId.Value);
        }

        var latestTransfers = await latestTransfersQuery
            .Select(st => new
            {
                st.Id,
                st.StatusId,
                StatusCode = st.Status.Code,
                StatusName = st.Status.Name,
                st.CreatedAt,
                st.SentAt,
                st.SourceBranchId,
                st.DestinationBranchId,
                SourceBranchName = st.SourceBranch != null
                    ? st.SourceBranch.Name
                    : null,
                DestinationBranchName = st.DestinationBranch.Name
            })
            .ToListAsync();
 
        return new
        {
            lowStockVariants,
            outOfStockVariants,
            transferInTransit,
            latestTransfers,
        };
    }

    public async Task<object> GetTopProductsAsync(DateTimeOffset startUtc, DateTimeOffset endUtcExclusive, int? branchId, bool isExecutiveAdmin)
    {
        var invoicesQuery = _db.Invoices
            .Include(i => i.Order)
            .Where(i => i.IssuedAt >= startUtc && i.IssuedAt < endUtcExclusive)
            .Where(i => i.InvoiceStatus.Code == InvoiceStatusCodes.FullyPaid);

        if (branchId.HasValue)
        {
            invoicesQuery = invoicesQuery.Where(i => i.Order != null && i.Order.BranchId == branchId.Value);
        }

        var topProductsQuery = from ii in _db.InvoiceItems
                               join inv in invoicesQuery on ii.InvoiceId equals inv.Id
                               join pv in _db.ProductVariants.Include(pv => pv.Product) on ii.ProductVariantId equals pv.Id
                               select new { ii.Quantity, pv, Order = inv.Order };

        if (branchId.HasValue)
        {
            topProductsQuery = topProductsQuery.Where(x => x.Order != null && x.Order.BranchId == branchId.Value);
        }

        var topProducts = await topProductsQuery
            .GroupBy(x => new
            {
                VariantId = x.pv.Id,
                ProductId = x.pv.Product.Id,
                ProductName = x.pv.Product.Name,
                VariantSku = x.pv.SKU
            })
            .Select(g => new
            {
                g.Key.ProductName,
                QuantitySold = g.Sum(x => x.Quantity)
            })
            .OrderByDescending(x => x.QuantitySold)
            .Take(10)
            .ToListAsync();

        return new { topProducts };
    }

    public async Task<object> GetDailySalesSummaryAsync(DateTimeOffset startUtc, DateTimeOffset endUtcExclusive, int? branchId, bool isExecutiveAdmin){
        var invoicesQuery = _db.Invoices
            .Include(i => i.Order)
            .Where(i => i.IssuedAt >= startUtc && i.IssuedAt < endUtcExclusive)
            .Where(i => i.InvoiceStatus.Code == InvoiceStatusCodes.FullyPaid);

        if (branchId.HasValue)
        {
            invoicesQuery = invoicesQuery.Where(i => i.Order != null && i.Order.BranchId == branchId.Value);
        }

        var totalRevenue = await invoicesQuery
            .GroupBy(i => i.IssuedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                Subtotal = g.Sum(i => i.Subtotal)
            })
            .OrderBy(x => x.Date)
            .ToListAsync();

        return new {
            totalRevenue
        };
    }

    public async Task<object> GetExpensesSummaryAsync(DateTimeOffset startUtc, DateTimeOffset endUtcExclusive, int? branchId)
    {
        var expensesQuery = _db.ExpenseLines
            .Include(el => el.Status)
            .Include(el => el.Expense)
            .Include(el => el.Currency)
            .Where(el => el.ExpensePeriodStart.HasValue && el.ExpensePeriodEnd.HasValue
                && el.ExpensePeriodStart.Value < endUtcExclusive
                && el.ExpensePeriodEnd.Value >= startUtc);

        if (branchId.HasValue)
        {
            expensesQuery = expensesQuery.Where(el => el.BranchId == branchId.Value);
        }

        var expensesByStatus = await expensesQuery
            .Select(el => new
            {
                StatusCode = el.Status.Code,
                el.CurrencyId,
                CurrencyCode = el.Currency != null ? el.Currency.Code : null,
                CurrencySymbol = el.Currency != null ? el.Currency.Symbol : null,
                el.TotalAmount,
                Paid = el.Payments
                    .Where(p => p.ApprovedAt != null && p.RejectionReason == null)
                    .Sum(p => (decimal?)p.Amount) ?? 0m
            })
            .GroupBy(x => x.StatusCode)
            .Select(g => new
            {
                StatusCode = g.Key,
                Count = g.Count(),
                Total = g.Sum(x => x.TotalAmount),
                Paid = g.Sum(x => x.Paid),
                Remaining = g.Sum(x => Math.Max(x.TotalAmount - x.Paid, 0m))
            })
            .ToListAsync();

        decimal draftedCount = expensesByStatus.FirstOrDefault(x => x.StatusCode == ExpenseStatusCodes.Draft)?.Count ?? 0;
        decimal partiallyPaidCount = expensesByStatus.FirstOrDefault(x => x.StatusCode == ExpenseStatusCodes.PartiallyPaid)?.Count ?? 0;
        decimal fullyPaidCount = expensesByStatus.FirstOrDefault(x => x.StatusCode == ExpenseStatusCodes.FullyPaid)?.Count ?? 0;

        var remainingByCurrency = await expensesQuery
            .Select(el => new
            {
                el.CurrencyId,
                CurrencyCode = el.Currency != null ? el.Currency.Code : null,
                CurrencySymbol = el.Currency != null ? el.Currency.Symbol : null,
                StatusCode = el.Status.Code,
                Remaining = Math.Max(
                    el.TotalAmount - ((decimal?)el.Payments
                        .Where(p => p.ApprovedAt != null && p.RejectionReason == null)
                        .Sum(p => p.Amount)) ?? 0m,
                    0m)
            })
            .Where(x => x.StatusCode == ExpenseStatusCodes.Draft || x.StatusCode == ExpenseStatusCodes.PartiallyPaid)
            .GroupBy(x => new { x.CurrencyId, x.CurrencyCode, x.CurrencySymbol })
            .Select(g => new
            {
                CurrencyId = g.Key.CurrencyId,
                CurrencyCode = g.Key.CurrencyCode,
                CurrencySymbol = g.Key.CurrencySymbol,
                Amount = g.Sum(x => x.Remaining)
            })
            .ToListAsync();

        return new
        {
            draftedCount,
            partiallyPaidCount,
            fullyPaidCount,
            remainingByCurrency
        };
    }

    public async Task<object> GetStockDemandTrendAsync(DateTimeOffset startUtc, DateTimeOffset endUtcExclusive, int? branchId, int? productId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // ── Current stock snapshot per product (for the product dropdown) ──
        var stockQuery = _db.LocationStocks
            .Include(ls => ls.ProductVariant)
                .ThenInclude(pv => pv.Product)
            .AsQueryable();

        if (branchId.HasValue)
            stockQuery = stockQuery.Where(ls => ls.BranchId == branchId.Value);

        var products = await stockQuery
            .Select(location => new
            {
                ProductId = location.ProductVariant.ProductId,
                ProductName = location.ProductVariant.Product.Name,
                StockQuantity = _db.BranchLotStocks
                    .Where(stock =>
                        stock.BranchId == location.BranchId &&
                        stock.InventoryLot.ProductVariantId == location.ProductVariantId &&
                        (!stock.InventoryLot.ExpiryDate.HasValue ||
                         stock.InventoryLot.ExpiryDate >= today))
                    .Sum(stock => (int?)stock.Quantity) ?? 0,
                location.ReorderLevel
            })
            .GroupBy(stock => new { stock.ProductId, stock.ProductName })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.ProductName,
                TotalStock = g.Sum(stock => stock.StockQuantity),
                TotalReorderLevel = g.Sum(stock => stock.ReorderLevel)
            })
            .OrderBy(x => x.ProductName)
            .ToListAsync();

        // If no product selected, return just the product list (no timeline)
        if (!productId.HasValue)
        {
            return new
            {
                products,
                selectedProduct = (object?)null,
                timeline = Array.Empty<object>()
            };
        }

        // ── Selected product stock info ──
        var selected = products.FirstOrDefault(p => p.ProductId == productId.Value);

        // ── Per-branch breakdown (only when "All Branch" is selected) ──
        var branchBreakdown = !branchId.HasValue
            ? await _db.LocationStocks
                .Include(ls => ls.Branch)
                .Include(ls => ls.ProductVariant)
                .Where(ls => ls.ProductVariant.ProductId == productId.Value)
                .Select(location => new
                {
                    location.BranchId,
                    BranchName = location.Branch.Name,
                    StockQuantity = _db.BranchLotStocks
                        .Where(stock =>
                            stock.BranchId == location.BranchId &&
                            stock.InventoryLot.ProductVariantId == location.ProductVariantId &&
                            (!stock.InventoryLot.ExpiryDate.HasValue ||
                             stock.InventoryLot.ExpiryDate >= today))
                        .Sum(stock => (int?)stock.Quantity) ?? 0,
                    location.ReorderLevel
                })
                .GroupBy(stock => new { stock.BranchId, stock.BranchName })
                .Select(g => new
                {
                    BranchId = g.Key.BranchId,
                    g.Key.BranchName,
                    Stock = g.Sum(stock => stock.StockQuantity),
                    ReorderLevel = g.Sum(stock => stock.ReorderLevel)
                })
                .OrderBy(x => x.BranchName)
                .ToListAsync()
            : null;

        // ── Demand: daily units sold for the selected product ──
        var invoicesQuery = _db.Invoices
            .Include(i => i.Order)
            .Where(i => i.IssuedAt >= startUtc && i.IssuedAt < endUtcExclusive)
            .Where(i => i.InvoiceStatus.Code == InvoiceStatusCodes.FullyPaid);

        if (branchId.HasValue)
            invoicesQuery = invoicesQuery.Where(i => i.Order != null && i.Order.BranchId == branchId.Value);

        var demandByDay = await (
            from ii in _db.InvoiceItems
            join inv in invoicesQuery on ii.InvoiceId equals inv.Id
            join pv in _db.ProductVariants on ii.ProductVariantId equals pv.Id
            where pv.ProductId == productId.Value
            group new { ii.Quantity } by inv.IssuedAt.Date into g
            select new
            {
                Date = g.Key,
                UnitsSold = g.Sum(x => x.Quantity)
            }
        ).ToListAsync();

        // ── Build daily timeline (zero-filled) ──
        var allDates = new List<DateTime>();
        for (var d = startUtc.UtcDateTime.Date; d < endUtcExclusive.UtcDateTime.Date; d = d.AddDays(1))
            allDates.Add(d);

        var demandLookup = demandByDay.ToDictionary(x => x.Date, x => x.UnitsSold);

        var timeline = allDates.Select(date => new
        {
            Date = date,
            UnitsSold = demandLookup.ContainsKey(date) ? demandLookup[date] : 0
        }).ToList();

        return new
        {
            products,
            selectedProduct = selected != null ? new
            {
                selected.ProductId,
                selected.ProductName,
                selected.TotalStock,
                selected.TotalReorderLevel
            } : (object?)null,
            timeline,
            branchBreakdown
        };
    }
}
