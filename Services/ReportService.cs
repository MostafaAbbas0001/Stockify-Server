using ClosedXML.Excel;

namespace Stockify.Services;

/// <summary>Builds the Excel (.xlsx) reports with ClosedXML; queries run through the tenant-filtered context.</summary>
public class ReportService
{
    private readonly AppDbContext _db;

    public ReportService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ReportFile> ExportStockOnHandAsync(CancellationToken ct = default)
    {
        // Build Excel
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("StockOnHand");

        // Headers
        var headers = new[]
        {
            "Branch", "Brand", "Product", "SKU", "Attributes", "Description",
            "Barcode", "Lot", "Manufactured", "Expiry", "Stock Qty"
        };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }
        ws.SheetView.FreezeRows(1);

        var stockItems = await (from stock in _db.BranchLotStocks.AsNoTracking()
                                join lot in _db.InventoryLots.AsNoTracking() on stock.InventoryLotId equals lot.Id
                                join pv in _db.ProductVariants.AsNoTracking() on lot.ProductVariantId equals pv.Id
                                join br in _db.Branches.AsNoTracking() on stock.BranchId equals br.Id
                                join p in _db.Products.AsNoTracking() on pv.ProductId equals p.Id
                                join b in _db.Brands.AsNoTracking() on p.BrandId equals b.Id into brandJoin
                                from b in brandJoin.DefaultIfEmpty()
                                where stock.Quantity > 0
                                select new
                                {
                                    BranchName = br.Name,
                                    BrandName = b != null ? b.Name : string.Empty,
                                    ProductName = p.Name,
                                    SKU = pv.SKU,
                                    Attributes = pv.AttributeValues
                                        .OrderBy(value => value.ProductAttribute.Name)
                                        .Select(value => new
                                        {
                                            Name = value.ProductAttribute.Name,
                                            value.Value
                                        })
                                        .ToList(),
                                    ProductDescription = p.Description,
                                    Barcode = pv.Barcode,
                                    lot.LotNumber,
                                    lot.ManufacturingDate,
                                    lot.ExpiryDate,
                                    StockQuantity = stock.Quantity
                                }).ToListAsync(ct);

        int row = 2;
        foreach (var item in stockItems)
        {
            ws.Cell(row, 1).Value = item.BranchName ?? string.Empty;
            ws.Cell(row, 2).Value = item.BrandName ?? string.Empty;
            ws.Cell(row, 3).Value = item.ProductName ?? string.Empty;
            ws.Cell(row, 4).Value = item.SKU ?? string.Empty;
            ws.Cell(row, 5).Value = string.Join(", ", item.Attributes.Select(attribute => $"{attribute.Name}: {attribute.Value}"));
            ws.Cell(row, 6).Value = item.ProductDescription ?? string.Empty;
            ws.Cell(row, 7).Value = item.Barcode ?? string.Empty;
            ws.Cell(row, 8).Value = item.LotNumber ?? string.Empty;
            ws.Cell(row, 9).Value = item.ManufacturingDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            ws.Cell(row, 10).Value = item.ExpiryDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            ws.Cell(row, 11).Value = item.StockQuantity;
            row++;
        }

        // Formatting
        ws.Column(11).Style.NumberFormat.Format = "0";
        ws.Columns().AdjustToContents();
        ws.RangeUsed()?.SetAutoFilter();

        // Return file
        using var stream = new MemoryStream();
        wb.SaveAs(stream);

        var stamp = DateTime.Now.ToString("ddMMyyyy");
        return new ReportFile(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Stock_In_Hand_{stamp}.xlsx"
        );
    }

    public async Task<ReportFile> ExportLowStockAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lowStocks = await _db.LocationStocks
            .AsNoTracking()
            .Select(location => new
            {
                BranchName = location.Branch.Name,
                BrandName = location.ProductVariant.Product.Brand.Name,
                ProductName = location.ProductVariant.Product.Name,
                location.ProductVariant.SKU,
                Attributes = location.ProductVariant.AttributeValues
                    .OrderBy(value => value.ProductAttribute.Name)
                    .Select(value => new
                    {
                        Name = value.ProductAttribute.Name,
                        value.Value
                    })
                    .ToList(),
                location.ProductVariant.Barcode,
                StockQuantity = _db.BranchLotStocks
                    .Where(stock =>
                        stock.BranchId == location.BranchId &&
                        stock.InventoryLot.ProductVariantId == location.ProductVariantId &&
                        (!stock.InventoryLot.ExpiryDate.HasValue ||
                         stock.InventoryLot.ExpiryDate >= today))
                    .Sum(stock => (int?)stock.Quantity) ?? 0,
                location.ReorderLevel
            })
            .Where(stock =>
                stock.ReorderLevel > 0 &&
                stock.StockQuantity < stock.ReorderLevel)
            .OrderBy(stock => stock.BranchName)
            .ThenBy(stock => stock.ProductName)
            .Select(stock => new
            {
                stock.BranchName,
                stock.BrandName,
                stock.ProductName,
                stock.SKU,
                stock.Attributes,
                stock.Barcode,
                stock.StockQuantity,
                stock.ReorderLevel,
                Deficit = stock.ReorderLevel - stock.StockQuantity
            })
            .ToListAsync(ct);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("LowStock");

        var headers = new[] { "Branch", "Brand", "Product", "SKU", "Attributes", "Barcode", "Stock Qty", "Reorder Level", "Deficit" };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }
        ws.SheetView.FreezeRows(1);

        int row = 2;
        foreach (var item in lowStocks)
        {
            ws.Cell(row, 1).Value = item.BranchName ?? string.Empty;
            ws.Cell(row, 2).Value = item.BrandName ?? string.Empty;
            ws.Cell(row, 3).Value = item.ProductName ?? string.Empty;
            ws.Cell(row, 4).Value = item.SKU ?? string.Empty;
            ws.Cell(row, 5).Value = string.Join(", ", item.Attributes.Select(attribute => $"{attribute.Name}: {attribute.Value}"));
            ws.Cell(row, 6).Value = item.Barcode ?? string.Empty;
            ws.Cell(row, 7).Value = item.StockQuantity;
            ws.Cell(row, 8).Value = item.ReorderLevel;
            ws.Cell(row, 9).Value = item.Deficit;
            row++;
        }

        ws.Column(7).Style.NumberFormat.Format = "0";
        ws.Column(8).Style.NumberFormat.Format = "0";
        ws.Column(9).Style.NumberFormat.Format = "0";
        ws.Columns().AdjustToContents();
        ws.RangeUsed()?.SetAutoFilter();

        using var stream = new MemoryStream();
        wb.SaveAs(stream);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd");
        return new ReportFile(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Low_Stock_{stamp}.xlsx"
        );
    }

    public async Task<ReportFile> ExportDailySalesAsync(DateTime date, int? branchId, CancellationToken ct = default)
    {
        if (date == default)
        {
            throw new ArgumentException("date is required in format YYYY-MM-DD");
        }

        var targetDateUtc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        var nextDateUtc = targetDateUtc.AddDays(1);

        var invoicesQuery = _db.Invoices
            .AsNoTracking()
            .Include(i => i.Order)
                .ThenInclude(o => o.Branch)
            .Include(i => i.Order)
                .ThenInclude(o => o.Customer)
            .Include(i => i.InvoiceStatus)
            .Include(i => i.Payments)
            .Where(i => i.IssuedAt >= targetDateUtc && i.IssuedAt < nextDateUtc);

        if (branchId.HasValue)
        {
            invoicesQuery = invoicesQuery.Where(i => i.Order.BranchId == branchId.Value);
        }

        var invoices = await invoicesQuery
            .Select(i => new
            {
                Date = i.IssuedAt,
                Branch = i.Order.Branch.Name,
                OrderNumber = i.Order.OrderNumber,
                InvoiceNumber = i.InvoiceNumber,
                Customer = i.Order.Customer != null ? i.Order.Customer.Name : string.Empty,
                Subtotal = i.Subtotal,
                Tax = i.TaxAmount,
                Delivery = i.DeliveryChargeAmount,
                Discount = i.DiscountAmount,
                Total = i.TotalAmount,
                Paid = i.Payments.Sum(p => p.Amount),
                Status = i.InvoiceStatus != null ? i.InvoiceStatus.Name : string.Empty
            })
            .OrderByDescending(i => i.Date)
            .ToListAsync(ct);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("DailySales");

        var headers = new[] { "Date", "Time", "Branch", "Order #", "Invoice #", "Customer", "Subtotal", "Tax", "Delivery", "Discount", "Total", "Remaining Dues", "Status" };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }
        ws.SheetView.FreezeRows(1);

        int row = 2;
        foreach (var inv in invoices)
        {
            var localDateTime = inv.Date.ToLocalTime();
            ws.Cell(row, 1).Value = localDateTime.Date;
            ws.Cell(row, 2).Value = localDateTime;
            ws.Cell(row, 3).Value = inv.Branch ?? string.Empty;
            ws.Cell(row, 4).Value = inv.OrderNumber ?? string.Empty;
            ws.Cell(row, 5).Value = inv.InvoiceNumber ?? string.Empty;
            ws.Cell(row, 6).Value = inv.Customer ?? string.Empty;
            ws.Cell(row, 7).Value = inv.Subtotal;
            ws.Cell(row, 8).Value = inv.Tax;
            ws.Cell(row, 9).Value = inv.Delivery;
            ws.Cell(row, 10).Value = inv.Discount;
            ws.Cell(row, 11).Value = inv.Total;

            var remaining = inv.Total - inv.Paid;
            ws.Cell(row, 12).Value = remaining;

            var statusCell = ws.Cell(row, 13);
            statusCell.Value = inv.Status ?? string.Empty;
            var statusLower = (inv.Status ?? string.Empty).ToLowerInvariant();
            switch (statusLower)
            {
                case "issued":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightGray;
                    break;
                case "partially paid":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightYellow;
                    break;
                case "fully paid":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightGreen;
                    break;
                case "void":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightSlateGray;
                    break;
                case "written off":
                    statusCell.Style.Fill.BackgroundColor = XLColor.CadetBlue;
                    break;
                case "refunded":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightBlue;
                    break;
                case "partially refunded":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightCyan;
                    break;
                default:
                    statusCell.Style.Fill.BackgroundColor = XLColor.White;
                    break;
            }
            row++;
        }

        // Formatting
        ws.Column(1).Style.DateFormat.Format = "yyyy-mm-dd";
        ws.Column(2).Style.DateFormat.Format = "HH:mm:ss";
        ws.Columns(7, 12).Style.NumberFormat.Format = "#,##0.00";
        ws.Columns().AdjustToContents();
        ws.RangeUsed()?.SetAutoFilter();

        using var stream = new MemoryStream();
        wb.SaveAs(stream);

        var stamp = targetDateUtc.ToString("yyyyMMdd");
        var branchPart = branchId.HasValue ? $"_Branch_{branchId.Value}" : string.Empty;
        return new ReportFile(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Daily_Sales_{stamp}{branchPart}.xlsx"
        );
    }

    public async Task<ReportFile> ExportMonthlySalesAsync(DateTime startDate, DateTime? endDate, int? branchId, CancellationToken ct = default)
    {
        if (startDate == default)
        {
            throw new ArgumentException("startDate is required in format YYYY-MM-DD");
        }

        var rangeStartUtc = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        var rangeEndUtc = DateTime.SpecifyKind((endDate ?? startDate).Date, DateTimeKind.Utc).AddDays(1);

        var invoicesQuery = _db.Invoices
            .AsNoTracking()
            .Include(i => i.Order)
                .ThenInclude(o => o.Branch)
            .Include(i => i.Order)
                .ThenInclude(o => o.Customer)
            .Include(i => i.InvoiceStatus)
            .Include(i => i.Payments)
            .Where(i => i.IssuedAt >= rangeStartUtc && i.IssuedAt < rangeEndUtc);

        if (branchId.HasValue)
        {
            invoicesQuery = invoicesQuery.Where(i => i.Order.BranchId == branchId.Value);
        }

        var invoices = await invoicesQuery
            .Select(i => new
            {
                Date = i.IssuedAt,
                Branch = i.Order.Branch.Name,
                OrderNumber = i.Order.OrderNumber,
                InvoiceNumber = i.InvoiceNumber,
                Customer = i.Order.Customer != null ? i.Order.Customer.Name : string.Empty,
                Subtotal = i.Subtotal,
                Tax = i.TaxAmount,
                Delivery = i.DeliveryChargeAmount,
                Discount = i.DiscountAmount,
                Total = i.TotalAmount,
                Paid = i.Payments.Sum(p => p.Amount),
                Status = i.InvoiceStatus != null ? i.InvoiceStatus.Name : string.Empty
            })
            .OrderByDescending(i => i.Date)
            .ToListAsync(ct);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("MonthlySales");

        var headers = new[] { "Date", "Time", "Branch", "Order #", "Invoice #", "Customer", "Subtotal", "Tax", "Delivery", "Discount", "Total", "Remaining Dues", "Status" };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }
        ws.SheetView.FreezeRows(1);

        int row = 2;
        foreach (var inv in invoices)
        {
            var localDateTime = inv.Date.ToLocalTime();
            ws.Cell(row, 1).Value = localDateTime.Date;
            ws.Cell(row, 2).Value = localDateTime;
            ws.Cell(row, 3).Value = inv.Branch ?? string.Empty;
            ws.Cell(row, 4).Value = inv.OrderNumber ?? string.Empty;
            ws.Cell(row, 5).Value = inv.InvoiceNumber ?? string.Empty;
            ws.Cell(row, 6).Value = inv.Customer ?? string.Empty;
            ws.Cell(row, 7).Value = inv.Subtotal;
            ws.Cell(row, 8).Value = inv.Tax;
            ws.Cell(row, 9).Value = inv.Delivery;
            ws.Cell(row, 10).Value = inv.Discount;
            ws.Cell(row, 11).Value = inv.Total;

            var remaining = inv.Total - inv.Paid;
            ws.Cell(row, 12).Value = remaining;

            var statusCell = ws.Cell(row, 13);
            statusCell.Value = inv.Status ?? string.Empty;
            var statusLower = (inv.Status ?? string.Empty).ToLowerInvariant();
            switch (statusLower)
            {
                case "issued":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightGray;
                    break;
                case "partially paid":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightYellow;
                    break;
                case "fully paid":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightGreen;
                    break;
                case "void":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightSlateGray;
                    break;
                case "written off":
                    statusCell.Style.Fill.BackgroundColor = XLColor.CadetBlue;
                    break;
                case "refunded":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightBlue;
                    break;
                case "partially refunded":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightCyan;
                    break;
                default:
                    statusCell.Style.Fill.BackgroundColor = XLColor.White;
                    break;
            }
            row++;
        }

        ws.Column(1).Style.DateFormat.Format = "yyyy-mm-dd";
        ws.Column(2).Style.DateFormat.Format = "HH:mm:ss";
        ws.Columns(7, 12).Style.NumberFormat.Format = "#,##0.00";
        ws.Columns().AdjustToContents();
        ws.RangeUsed()?.SetAutoFilter();

        using var stream = new MemoryStream();
        wb.SaveAs(stream);

        var stampStart = rangeStartUtc.ToString("yyyyMMdd");
        var stampEnd = rangeEndUtc.AddDays(-1).ToString("yyyyMMdd");
        var branchPart = branchId.HasValue ? $"_Branch_{branchId.Value}" : string.Empty;
        return new ReportFile(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Monthly_Sales_{stampStart}_to_{stampEnd}{branchPart}.xlsx"
        );
    }

    public async Task<ReportFile> ExportBranchPerformanceAsync(DateTime startDate, DateTime? endDate, int branchId, CancellationToken ct = default)
    {
        if (startDate == default)
        {
            throw new ArgumentException("startDate is required in format YYYY-MM-DD");
        }
        if (branchId <= 0)
        {
            throw new ArgumentException("branchId is required");
        }

        var branch = await _db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId, ct);
        if (branch == null)
        {
            throw new KeyNotFoundException("Branch not found");
        }

        var rangeStartUtc = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        var rangeEndUtc = DateTime.SpecifyKind((endDate ?? startDate).Date, DateTimeKind.Utc).AddDays(1);

        var invoices = await _db.Invoices
            .AsNoTracking()
            .Include(i => i.Order)
                .ThenInclude(o => o.Customer)
            .Include(i => i.InvoiceStatus)
            .Include(i => i.Payments)
            .Where(i => i.Order.BranchId == branchId && i.IssuedAt >= rangeStartUtc && i.IssuedAt < rangeEndUtc)
            .Select(i => new
            {
                Date = i.IssuedAt,
                OrderNumber = i.Order.OrderNumber,
                InvoiceNumber = i.InvoiceNumber,
                Customer = i.Order.Customer != null ? i.Order.Customer.Name : string.Empty,
                Subtotal = i.Subtotal,
                Tax = i.TaxAmount,
                Delivery = i.DeliveryChargeAmount,
                Discount = i.DiscountAmount,
                Total = i.TotalAmount,
                Paid = i.Payments.Sum(p => p.Amount),
                Status = i.InvoiceStatus != null ? i.InvoiceStatus.Name : string.Empty
            })
            .OrderByDescending(i => i.Date)
            .ToListAsync(ct);

        var totalInvoices = invoices.Count;
        var totalSales = invoices.Sum(i => i.Total);
        var totalTax = invoices.Sum(i => i.Tax);
        var totalDelivery = invoices.Sum(i => i.Delivery);
        var totalDiscount = invoices.Sum(i => i.Discount);
        var totalPaid = invoices.Sum(i => i.Paid);
        var totalRemaining = totalSales - totalPaid;
        var avgOrder = totalInvoices > 0 ? totalSales / totalInvoices : 0m;
        var collectionRate = totalSales > 0 ? totalPaid / totalSales : 0m;

        using var wb = new XLWorkbook();

        // Summary sheet
        var summary = wb.Worksheets.Add("Summary");
        summary.Cell(1, 1).Value = "Branch Performance Summary";
        summary.Cell(1, 1).Style.Font.Bold = true;
        summary.Cell(2, 1).Value = "Branch";
        summary.Cell(2, 2).Value = branch.Name;
        summary.Cell(3, 1).Value = "Date Range";
        summary.Cell(3, 2).Value = $"{rangeStartUtc:yyyy-MM-dd} to {rangeEndUtc.AddDays(-1):yyyy-MM-dd}";

        var metrics = new (string Label, decimal Value, string? Format)[]
        {
            ("Total Invoices", totalInvoices, "0"),
            ("Total Sales", totalSales, "#,##0.00"),
            ("Total Tax", totalTax, "#,##0.00"),
            ("Total Delivery", totalDelivery, "#,##0.00"),
            ("Total Discount", totalDiscount, "#,##0.00"),
            ("Total Paid", totalPaid, "#,##0.00"),
            ("Remaining Dues", totalRemaining, "#,##0.00"),
            ("Avg Order Value", avgOrder, "#,##0.00"),
            ("Collection Rate", collectionRate, "0.00%"),
        };

        int summaryRow = 5;
        foreach (var (label, value, format) in metrics)
        {
            summary.Cell(summaryRow, 1).Value = label;
            summary.Cell(summaryRow, 2).Value = value;
            if (!string.IsNullOrEmpty(format))
            {
                summary.Cell(summaryRow, 2).Style.NumberFormat.Format = format;
            }
            summaryRow++;
        }
        summary.Columns().AdjustToContents();

        // Detail sheet
        var ws = wb.Worksheets.Add("Details");
        var headers = new[] { "Date", "Time", "Order #", "Invoice #", "Customer", "Subtotal", "Tax", "Delivery", "Discount", "Total", "Paid", "Remaining", "Status" };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }
        ws.SheetView.FreezeRows(1);

        int row = 2;
        foreach (var inv in invoices)
        {
            var localDateTime = inv.Date.ToLocalTime();
            ws.Cell(row, 1).Value = localDateTime.Date;
            ws.Cell(row, 2).Value = localDateTime;
            ws.Cell(row, 3).Value = inv.OrderNumber ?? string.Empty;
            ws.Cell(row, 4).Value = inv.InvoiceNumber ?? string.Empty;
            ws.Cell(row, 5).Value = inv.Customer ?? string.Empty;
            ws.Cell(row, 6).Value = inv.Subtotal;
            ws.Cell(row, 7).Value = inv.Tax;
            ws.Cell(row, 8).Value = inv.Delivery;
            ws.Cell(row, 9).Value = inv.Discount;
            ws.Cell(row, 10).Value = inv.Total;
            ws.Cell(row, 11).Value = inv.Paid;
            ws.Cell(row, 12).Value = inv.Total - inv.Paid;
            var statusCell = ws.Cell(row, 13);
            statusCell.Value = inv.Status ?? string.Empty;
            row++;
        }

        ws.Column(1).Style.DateFormat.Format = "yyyy-mm-dd";
        ws.Column(2).Style.DateFormat.Format = "HH:mm:ss";
        ws.Columns(6, 12).Style.NumberFormat.Format = "#,##0.00";
        ws.Columns().AdjustToContents();
        ws.RangeUsed()?.SetAutoFilter();

        using var stream = new MemoryStream();
        wb.SaveAs(stream);

        var stampStart = rangeStartUtc.ToString("yyyyMMdd");
        var stampEnd = rangeEndUtc.AddDays(-1).ToString("yyyyMMdd");
        return new ReportFile(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Branch_Performance_{branch.Name}_{stampStart}_to_{stampEnd}.xlsx"
        );
    }

    public async Task<ReportFile> ExportStaffPerformanceAsync(DateTime startDate, DateTime? endDate, int employeeId, CancellationToken ct = default)
    {
        if (startDate == default)
        {
            throw new ArgumentException("startDate is required in format YYYY-MM-DD");
        }
        if (employeeId <= 0)
        {
            throw new ArgumentException("employeeId is required");
        }

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee == null)
        {
            throw new KeyNotFoundException("Employee not found");
        }

        var rangeStartUtc = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        var rangeEndUtc = DateTime.SpecifyKind((endDate ?? startDate).Date, DateTimeKind.Utc).AddDays(1);

        var invoices = await _db.Invoices
            .AsNoTracking()
            .Include(i => i.Order)
                .ThenInclude(o => o.CreatedByEmployee)
            .Include(i => i.Order)
                .ThenInclude(o => o.Customer)
            .Include(i => i.Payments)
            .Include(i => i.InvoiceStatus)
            .Where(i => i.Order.CreatedByEmployeeId == employeeId && i.IssuedAt >= rangeStartUtc && i.IssuedAt < rangeEndUtc)
            .Select(i => new
            {
                Date = i.IssuedAt,
                OrderNumber = i.Order.OrderNumber,
                InvoiceNumber = i.InvoiceNumber,
                EmployeeName = i.Order.CreatedByEmployee != null ? i.Order.CreatedByEmployee.Name : "Unassigned",
                Customer = i.Order.Customer != null ? i.Order.Customer.Name : string.Empty,
                Subtotal = i.Subtotal,
                Tax = i.TaxAmount,
                Delivery = i.DeliveryChargeAmount,
                Discount = i.DiscountAmount,
                Total = i.TotalAmount,
                Paid = i.Payments.Sum(p => p.Amount),
                Status = i.InvoiceStatus != null ? i.InvoiceStatus.Name : string.Empty
            })
            .ToListAsync(ct);

        var grouped = invoices
            .GroupBy(i => i.EmployeeName)
            .Select(g => new
            {
                Employee = g.Key,
                InvoiceCount = g.Count(),
                TotalSales = g.Sum(x => x.Subtotal),
                AvgOrder = g.Count() > 0 ? g.Sum(x => x.Subtotal) / g.Count() : 0m
            })
            .OrderByDescending(x => x.TotalSales)
            .ToList();

        using var wb = new XLWorkbook();

        var sheet = wb.Worksheets.Add("StaffPerformance");

        var detailHeaders = new[] { "Date", "Time", "Employee", "Order #", "Invoice #", "Customer", "Subtotal", "Tax", "Delivery", "Discount", "Total", "Paid", "Remaining", "Status" };
        for (int i = 0; i < detailHeaders.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = detailHeaders[i];
            sheet.Cell(1, i + 1).Style.Font.Bold = true;
        }
        sheet.SheetView.FreezeRows(1);

        int dRow = 2;
        foreach (var inv in invoices.OrderByDescending(i => i.Date))
        {
            var localDateTime = inv.Date.ToLocalTime();
            sheet.Cell(dRow, 1).Value = localDateTime.Date;
            sheet.Cell(dRow, 2).Value = localDateTime;
            sheet.Cell(dRow, 3).Value = inv.EmployeeName;
            sheet.Cell(dRow, 4).Value = inv.OrderNumber ?? string.Empty;
            sheet.Cell(dRow, 5).Value = inv.InvoiceNumber ?? string.Empty;
            sheet.Cell(dRow, 6).Value = inv.Customer;
            sheet.Cell(dRow, 7).Value = inv.Subtotal;
            sheet.Cell(dRow, 8).Value = inv.Tax;
            sheet.Cell(dRow, 9).Value = inv.Delivery;
            sheet.Cell(dRow, 10).Value = inv.Discount;
            sheet.Cell(dRow, 11).Value = inv.Total;
            sheet.Cell(dRow, 12).Value = inv.Paid;
            sheet.Cell(dRow, 13).Value = inv.Total - inv.Paid;
            var statusCell = sheet.Cell(dRow, 14);
            statusCell.Value = inv.Status ?? string.Empty;
            var statusLower = (inv.Status ?? string.Empty).ToLowerInvariant();
            switch (statusLower)
            {
                case "issued":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightGray;
                    break;
                case "partially paid":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightYellow;
                    break;
                case "fully paid":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightGreen;
                    break;
                case "void":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightSlateGray;
                    break;
                case "written off":
                    statusCell.Style.Fill.BackgroundColor = XLColor.CadetBlue;
                    break;
                case "refunded":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightBlue;
                    break;
                case "partially refunded":
                    statusCell.Style.Fill.BackgroundColor = XLColor.LightCyan;
                    break;
                default:
                    statusCell.Style.Fill.BackgroundColor = XLColor.White;
                    break;
            }
            dRow++;
        }

        sheet.Column(1).Style.DateFormat.Format = "yyyy-mm-dd";
        sheet.Column(2).Style.DateFormat.Format = "HH:mm:ss";
        sheet.Columns(7, 13).Style.NumberFormat.Format = "#,##0.00";
        sheet.Columns().AdjustToContents();
        sheet.RangeUsed()?.SetAutoFilter();

        using var stream = new MemoryStream();
        wb.SaveAs(stream);

        var stampStart = rangeStartUtc.ToString("yyyyMMdd");
        var stampEnd = rangeEndUtc.AddDays(-1).ToString("yyyyMMdd");
        return new ReportFile(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Staff_Performance_{employee.Name}_{stampStart}_to_{stampEnd}.xlsx"
        );
    }

    public async Task<ReportFile> ExportOrdersAsync(DateTime startDate, DateTime? endDate, CancellationToken ct = default)
    {
        if (startDate == default)
        {
            throw new ArgumentException("startDate is required in format YYYY-MM-DD");
        }

        var rangeStartUtc = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        var rangeEndUtc = DateTime.SpecifyKind((endDate ?? startDate).Date, DateTimeKind.Utc).AddDays(1);

        var orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Branch)
            .Include(o => o.Customer)
            .Include(o => o.CreatedByEmployee)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.ProductVariant)
                    .ThenInclude(pv => pv.Product)
            .Where(o => o.CreatedAt >= rangeStartUtc && o.CreatedAt < rangeEndUtc)
            .ToListAsync(ct);

        using var wb = new XLWorkbook();

        // Orders sheet
        var ordersSheet = wb.Worksheets.Add("Orders");
        var orderHeaders = new[] { "Order #", "Branch", "Customer", "Employee", "Created At", "Items" };
        for (int i = 0; i < orderHeaders.Length; i++)
        {
            ordersSheet.Cell(1, i + 1).Value = orderHeaders[i];
            ordersSheet.Cell(1, i + 1).Style.Font.Bold = true;
        }
        ordersSheet.SheetView.FreezeRows(1);

        int oRow = 2;
        foreach (var order in orders.OrderByDescending(o => o.CreatedAt))
        {
            var itemsSummary = string.Join(", ", order.OrderItems.Select(oi =>
                $"{(oi.ProductVariant?.Product?.Name ?? oi.ProductVariant?.SKU ?? "Item")} x{oi.Quantity}"));

            ordersSheet.Cell(oRow, 1).Value = order.OrderNumber;
            ordersSheet.Cell(oRow, 2).Value = order.Branch?.Name ?? string.Empty;
            ordersSheet.Cell(oRow, 3).Value = order.Customer?.Name ?? string.Empty;
            ordersSheet.Cell(oRow, 4).Value = order.CreatedByEmployee?.Name ?? string.Empty;
            ordersSheet.Cell(oRow, 5).Value = order.CreatedAt.ToLocalTime();
            ordersSheet.Cell(oRow, 6).Value = itemsSummary;
            oRow++;
        }
        ordersSheet.Column(5).Style.DateFormat.Format = "yyyy-mm-dd HH:mm:ss";
        ordersSheet.Columns().AdjustToContents();
        ordersSheet.RangeUsed()?.SetAutoFilter();

        // Items sheet
        var itemsSheet = wb.Worksheets.Add("OrderItems");
        var itemHeaders = new[] { "Order #", "Branch", "Customer", "SKU", "Product", "Quantity" };
        for (int i = 0; i < itemHeaders.Length; i++)
        {
            itemsSheet.Cell(1, i + 1).Value = itemHeaders[i];
            itemsSheet.Cell(1, i + 1).Style.Font.Bold = true;
        }
        itemsSheet.SheetView.FreezeRows(1);

        int iRow = 2;
        foreach (var order in orders.OrderByDescending(o => o.CreatedAt))
        {
            var branchName = order.Branch?.Name ?? string.Empty;
            var customerName = order.Customer?.Name ?? string.Empty;
            foreach (var item in order.OrderItems)
            {
                itemsSheet.Cell(iRow, 1).Value = order.OrderNumber;
                itemsSheet.Cell(iRow, 2).Value = branchName;
                itemsSheet.Cell(iRow, 3).Value = customerName;
                itemsSheet.Cell(iRow, 4).Value = item.ProductVariant?.SKU ?? string.Empty;
                itemsSheet.Cell(iRow, 5).Value = item.ProductVariant?.Product?.Name ?? string.Empty;
                itemsSheet.Cell(iRow, 6).Value = item.Quantity;
                iRow++;
            }
        }
        itemsSheet.Columns().AdjustToContents();
        itemsSheet.RangeUsed()?.SetAutoFilter();

        using var stream = new MemoryStream();
        wb.SaveAs(stream);

        var stamp = rangeStartUtc.ToString("yyyyMMdd") + "_to_" + rangeEndUtc.AddDays(-1).ToString("yyyyMMdd");
        return new ReportFile(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Orders_{stamp}.xlsx"
        );
    }

    public async Task<ReportFile> ExportDeliveryAsync(int? deliveryLocationId, CancellationToken ct = default)
    {
        var ordersQuery = _db.Orders
            .AsNoTracking()
            .Include(o => o.Branch)
            .Include(o => o.Customer)
            .Include(o => o.OrderStatus)
            .Include(o => o.DeliveryLocation)
            .Where(o => !o.DeliveryLocation.IsInStore);

        if (deliveryLocationId.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.DeliveryLocationId == deliveryLocationId.Value);
        }

        var orders = await ordersQuery
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.OrderNumber,
                Branch = o.Branch != null ? o.Branch.Name : string.Empty,
                Customer = o.Customer != null ? o.Customer.Name : string.Empty,
                Status = o.OrderStatus != null ? o.OrderStatus.Name : string.Empty,
                DeliveryLocation = o.DeliveryLocation != null ? o.DeliveryLocation.LocationName : string.Empty,
                o.CreatedAt
            })
            .ToListAsync(ct);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Delivery");
        var headers = new[] { "Order #", "Branch", "Customer", "Status", "Delivery Location", "Created At" };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }
        ws.SheetView.FreezeRows(1);

        int row = 2;
        foreach (var o in orders)
        {
            ws.Cell(row, 1).Value = o.OrderNumber;
            ws.Cell(row, 2).Value = o.Branch;
            ws.Cell(row, 3).Value = o.Customer;
            ws.Cell(row, 4).Value = o.Status;
            ws.Cell(row, 5).Value = o.DeliveryLocation;
            ws.Cell(row, 6).Value = o.CreatedAt.ToLocalTime();
            row++;
        }

        ws.Column(6).Style.DateFormat.Format = "yyyy-mm-dd HH:mm:ss";
        ws.Columns().AdjustToContents();
        ws.RangeUsed()?.SetAutoFilter();

        using var stream = new MemoryStream();
        wb.SaveAs(stream);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd");
        var dlPart = deliveryLocationId.HasValue ? $"_Location_{deliveryLocationId.Value}" : "_All";
        return new ReportFile(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Delivery_{stamp}{dlPart}.xlsx"
        );
    }

    public async Task<ReportFile> ExportCustomersAsync(DateTime startDate, DateTime? endDate, int? customerId, CancellationToken ct = default)
    {
        if (startDate == default)
        {
            throw new ArgumentException("startDate is required in format YYYY-MM-DD");
        }

        var rangeStartUtc = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        var rangeEndUtc = DateTime.SpecifyKind((endDate ?? startDate).Date, DateTimeKind.Utc).AddDays(1);

        var orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Branch)
            .Include(o => o.Invoices)
            .Where(o => o.CreatedAt >= rangeStartUtc && o.CreatedAt < rangeEndUtc)
            .Select(o => new
            {
                CustomerName = o.Customer != null ? o.Customer.Name : "Guest",
                Branch = o.Branch != null ? o.Branch.Name : string.Empty,
                o.OrderNumber,
                o.CreatedAt,
                Subtotal = o.Invoices.Sum(inv => inv.Subtotal)
            })
            .ToListAsync(ct);

        if (customerId.HasValue)
        {
            // If specific customer requested, filter after projection (since optional guest string)
            var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId.Value, ct);
            if (customer == null)
            {
                throw new KeyNotFoundException("Customer not found");
            }
            orders = orders.Where(o => o.CustomerName == customer.Name).ToList();
        }

        var summaryData = orders
            .GroupBy(o => o.CustomerName)
            .Select(g => new
            {
                Customer = g.Key,
                OrdersCount = g.Count(),
                TotalPaid = g.Sum(x => x.Subtotal),
                FirstOrder = g.Min(x => x.CreatedAt),
                LastOrder = g.Max(x => x.CreatedAt)
            })
            .OrderByDescending(x => x.OrdersCount)
            .ThenBy(x => x.Customer)
            .ToList();

        using var wb = new XLWorkbook();

        var summary = wb.Worksheets.Add("Summary");
        var summaryHeaders = new[] { "Customer", "Orders", "Total Paid (Subtotal)", "First Order", "Last Order" };
        for (int i = 0; i < summaryHeaders.Length; i++)
        {
            summary.Cell(1, i + 1).Value = summaryHeaders[i];
            summary.Cell(1, i + 1).Style.Font.Bold = true;
        }
        summary.SheetView.FreezeRows(1);

        int sRow = 2;
        foreach (var s in summaryData)
        {
            summary.Cell(sRow, 1).Value = s.Customer;
            summary.Cell(sRow, 2).Value = s.OrdersCount;
            summary.Cell(sRow, 3).Value = s.TotalPaid;
            summary.Cell(sRow, 4).Value = s.FirstOrder.ToLocalTime();
            summary.Cell(sRow, 5).Value = s.LastOrder.ToLocalTime();
            sRow++;
        }
        summary.Column(3).Style.NumberFormat.Format = "#,##0.00";
        summary.Columns(4, 5).Style.DateFormat.Format = "yyyy-mm-dd HH:mm:ss";
        summary.Columns().AdjustToContents();
        summary.RangeUsed()?.SetAutoFilter();

        var details = wb.Worksheets.Add("Orders");
        var detailHeaders = new[] { "Customer", "Order #", "Branch", "Created At", "Subtotal" };
        for (int i = 0; i < detailHeaders.Length; i++)
        {
            details.Cell(1, i + 1).Value = detailHeaders[i];
            details.Cell(1, i + 1).Style.Font.Bold = true;
        }
        details.SheetView.FreezeRows(1);

        int dRow = 2;
        foreach (var o in orders.OrderByDescending(o => o.CreatedAt))
        {
            details.Cell(dRow, 1).Value = o.CustomerName;
            details.Cell(dRow, 2).Value = o.OrderNumber;
            details.Cell(dRow, 3).Value = o.Branch;
            details.Cell(dRow, 4).Value = o.CreatedAt.ToLocalTime();
            details.Cell(dRow, 5).Value = o.Subtotal;
            dRow++;
        }
        details.Column(4).Style.DateFormat.Format = "yyyy-mm-dd HH:mm:ss";
        details.Column(5).Style.NumberFormat.Format = "#,##0.00";
        details.Columns().AdjustToContents();
        details.RangeUsed()?.SetAutoFilter();

        using var stream = new MemoryStream();
        wb.SaveAs(stream);

        var stamp = rangeStartUtc.ToString("yyyyMMdd") + "_to_" + rangeEndUtc.AddDays(-1).ToString("yyyyMMdd");
        return new ReportFile(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Customers_{stamp}.xlsx"
        );
    }

    public async Task<ReportFile> ExportExpensesAsync(DateTime? from, DateTime? to, int? categoryId, int? branchId, CancellationToken ct = default)
    {
        DateTime? fromUtc = from.HasValue ? DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc) : null;
        DateTime? toUtc = to.HasValue ? DateTime.SpecifyKind(to.Value.Date, DateTimeKind.Utc).AddDays(1) : null;

        var linesQuery = _db.ExpenseLines
            .AsNoTracking()
            .Include(l => l.Expense)
            .ThenInclude(e => e.ExpenseCategory)
            .Include(l => l.Category)
            .Include(l => l.Currency)
            .Include(l => l.Branch)
            .Include(l => l.Status)
            .Include(l => l.LineType)
            .Include(l => l.Payments)
            .ThenInclude(p => p.PaymentMethod)
            .Include(l => l.Payments)
            .ThenInclude(p => p.CreatedBy)
            .Include(l => l.Payments)
            .ThenInclude(p => p.ApprovedBy)
            .Include(l => l.Employee)
            .AsQueryable();

        if (fromUtc.HasValue)
            linesQuery = linesQuery.Where(l => l.Expense.CreatedAt >= fromUtc.Value);

        if (toUtc.HasValue)
            linesQuery = linesQuery.Where(l => l.Expense.CreatedAt < toUtc.Value);

        if (categoryId.HasValue)
            linesQuery = linesQuery.Where(l => l.CategoryId == categoryId.Value);

        if (branchId.HasValue)
            linesQuery = linesQuery.Where(l => l.BranchId == branchId.Value);

        var lines = await linesQuery
            .OrderByDescending(l => l.Expense.CreatedAt)
            .ThenByDescending(l => l.ExpenseId)
            .ToListAsync(ct);

        var expenseGroups = lines
            .GroupBy(l => l.ExpenseId)
            .Select(g => new
            {
                Expense = g.First().Expense,
                Lines = g.ToList(),
                TotalAmount = g.Sum(x => x.TotalAmount),
                PaidAmount = g.Sum(x => x.Payments
                    .Where(p => p.ApprovedAt != null && p.RejectionReason == null)
                    .Sum(p => p.Amount)),
                Payments = g.SelectMany(x => x.Payments)
                    .OrderBy(p => p.PaymentDate)
                    .ToList()
            })
            .OrderByDescending(x => x.Expense.CreatedAt)
            .ThenByDescending(x => x.Expense.Id)
            .ToList();

        using var wb = new XLWorkbook();

        // Summary sheet per expense
        var summary = wb.Worksheets.Add("Summary");
        var summaryHeaders = new[] { "Expense #", "Expense Date", "Main Category", "Total Amount", "Paid", "Remaining Dues", "Lines", "Line Details", "Created At", "Payments" };
        for (int i = 0; i < summaryHeaders.Length; i++)
        {
            summary.Cell(1, i + 1).Value = summaryHeaders[i];
            summary.Cell(1, i + 1).Style.Font.Bold = true;
        }
        summary.SheetView.FreezeRows(1);

        int sRow = 2;
        foreach (var exp in expenseGroups)
        {
            var totalsByCurrency = exp.Lines
                .GroupBy(l => l.Currency?.Code ?? "N/A")
                .ToDictionary(g => g.Key, g => g.Sum(x => x.TotalAmount));

            var paidByCurrency = exp.Payments
                .Where(p => p.ApprovedAt != null && p.RejectionReason == null)
                .GroupBy(p => p.ExpenseLine?.Currency?.Code ?? "N/A")
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

            string FormatAmounts(Dictionary<string, decimal> map)
                => string.Join(" / ", map.Select(kvp => kvp.Key == "N/A"
                    ? $"{kvp.Value:0.00}"
                    : $"{kvp.Value:0.00} {kvp.Key}"));

            var remainingByCurrency = totalsByCurrency.ToDictionary(kvp => kvp.Key, kvp => kvp.Value - (paidByCurrency.ContainsKey(kvp.Key) ? paidByCurrency[kvp.Key] : 0m));

            summary.Cell(sRow, 1).Value = exp.Expense.ExpenseNumber ?? $"EXP-{exp.Expense.Id}";
            summary.Cell(sRow, 2).Value = exp.Expense.CreatedAt.ToLocalTime();
            summary.Cell(sRow, 3).Value = exp.Expense.ExpenseCategory?.Name ?? string.Empty;
            summary.Cell(sRow, 4).Value = FormatAmounts(totalsByCurrency);
            summary.Cell(sRow, 5).Value = paidByCurrency.Count == 0 ? "0.00" : FormatAmounts(paidByCurrency);
            summary.Cell(sRow, 6).Value = FormatAmounts(remainingByCurrency);
            summary.Cell(sRow, 7).Value = exp.Lines.Count;
            var lineDetails = exp.Lines.Count == 0
                ? "None"
                : string.Join(Environment.NewLine, exp.Lines.Select(l =>
                    {
                        var currency = l.Currency?.Code;
                        var currencyPart = string.IsNullOrWhiteSpace(currency) ? string.Empty : $" {currency}";
                        var category = l.Category?.Name ?? "No Category";
                        return $"{l.TotalAmount:0.00}{currencyPart} [{category}]";
                    }));
            summary.Cell(sRow, 8).Value = lineDetails;
            summary.Cell(sRow, 8).Style.Alignment.WrapText = true;
            summary.Cell(sRow, 9).Value = exp.Expense.CreatedAt.ToLocalTime();
            if (exp.Payments.Count == 0)
            {
                summary.Cell(sRow, 10).Value = "None";
            }
            else
            {
                var rich = summary.Cell(sRow, 10).GetRichText();
                rich.ClearText();
                for (int i = 0; i < exp.Payments.Count; i++)
                {
                    var p = exp.Payments[i];
                    var status = p.ApprovedAt != null && p.RejectionReason == null
                        ? "Approved"
                        : p.RejectionReason != null ? $"Rejected: {p.RejectionReason}" : "Pending";
                    var statusColor = p.ApprovedAt != null && p.RejectionReason == null
                        ? XLColor.ForestGreen
                        : p.RejectionReason != null ? XLColor.Crimson : XLColor.DarkOrange;

                    var currency = p.ExpenseLine?.Currency?.Code;
                    var method = p.PaymentMethod?.Name ?? "Method?";
                    var currencyPart = string.IsNullOrWhiteSpace(currency) ? string.Empty : $" {currency}";
                    var createdBy = p.CreatedBy?.Username ?? "n/a";
                    var approvedBy = p.ApprovedBy?.Username ?? "n/a";
                    var statusText = p.ApprovedAt != null && p.RejectionReason == null
                        ? $"Approved by {approvedBy}"
                        : p.RejectionReason != null
                            ? $"Rejected: {p.RejectionReason} by {approvedBy}"
                            : $"Pending by {createdBy}";

                    var paymentRef = string.IsNullOrWhiteSpace(p.PaymentReference) ? null : p.PaymentReference;

                    var line = exp.Lines.FirstOrDefault(l => l.Id == p.ExpenseLineId);
                    var lineCategory = line?.Category?.Name;
                    var lineType = line?.LineType?.Name;
                    var lineParts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(lineCategory))
                        lineParts.Add($"Category: {lineCategory}");
                    if (!string.IsNullOrWhiteSpace(lineType))
                        lineParts.Add($"Type: {lineType}");
                    var lineInfo = lineParts.Count == 0 ? "line: n/a" : string.Join(" | ", lineParts);

                    var refInfo = paymentRef != null ? $"Ref: {paymentRef}" : null;

                    rich.AddText($"{p.Amount:0.00}{currencyPart} [{method}] (");
                    var statusSegment = rich.AddText(statusText);
                    statusSegment.SetFontColor(statusColor);
                    if (refInfo != null)
                        rich.AddText(") for " + lineInfo + " | " + refInfo);
                    else
                        rich.AddText(") for " + lineInfo);

                    if (i < exp.Payments.Count - 1)
                    {
                        rich.AddText(Environment.NewLine);
                    }
                }
                summary.Cell(sRow, 10).Style.Alignment.WrapText = true;
            }
            sRow++;
        }
        summary.Column(2).Style.DateFormat.Format = "yyyy-mm-dd";
        summary.Column(9).Style.DateFormat.Format = "yyyy-mm-dd HH:mm:ss";
        summary.Columns(4, 6).Style.NumberFormat.Format = "#,##0.00";
        summary.Columns().AdjustToContents();
        summary.RangeUsed()?.SetAutoFilter();
        summary.RangeUsed()?.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
        summary.RangeUsed()?.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);

        using var stream = new MemoryStream();
        wb.SaveAs(stream);

        var fromPart = fromUtc.HasValue ? fromUtc.Value.ToString("yyyyMMdd") : "All";
        var toPart = toUtc.HasValue ? toUtc.Value.AddDays(-1).ToString("yyyyMMdd") : "All";
        var branchPart = branchId.HasValue ? $"_Branch_{branchId.Value}" : string.Empty;
        var categoryPart = categoryId.HasValue ? $"_Category_{categoryId.Value}" : string.Empty;

        return new ReportFile(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Expense_Report_{fromPart}_to_{toPart}{branchPart}{categoryPart}.xlsx"
        );
    }
}
