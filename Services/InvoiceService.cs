using System.Data;

namespace Stockify.Services;

public class InvoiceService
{
    private const int PageSize = 20;
    private readonly AppDbContext _db;
    private readonly InventoryLotService _inventory;
    private readonly DocumentNumberService _documentNumbers;
    private readonly LookupService _lookups;

    public InvoiceService(
        AppDbContext db,
        InventoryLotService inventory,
        DocumentNumberService documentNumbers,
        LookupService lookups)
    {
        _db = db;
        _inventory = inventory;
        _documentNumbers = documentNumbers;
        _lookups = lookups;
    }

    private static decimal R2(decimal x) => Math.Round(x, 2, MidpointRounding.AwayFromZero);

    public async Task<object> GetAllInvoicesAsync(
        string? search,
        int? statusId,
        int? customerId,
        int? orderId,
        int? branchId,
        DateTime? startDate,
        DateTime? endDate,
        int page)
    {
        page = Math.Max(page, 1);
        var query = _db.Invoices
            .AsNoTracking()
            .Include(i => i.Order)
                .ThenInclude(o => o.Customer)
            .Include(i => i.InvoiceStatus)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().ToLower()}%";
            query = query.Where(invoice =>
                EF.Functions.Like(invoice.InvoiceNumber.ToLower(), pattern) ||
                EF.Functions.Like(invoice.Order.OrderNumber.ToLower(), pattern) ||
                (invoice.Order.Customer != null &&
                 EF.Functions.Like(invoice.Order.Customer.Name.ToLower(), pattern)));
        }

        if (statusId.HasValue)
            query = query.Where(i => i.InvoiceStatusId == statusId.Value);
        if (customerId.HasValue)
            query = query.Where(invoice => invoice.Order.CustomerId == customerId.Value);
        if (orderId.HasValue)
            query = query.Where(invoice => invoice.OrderId == orderId.Value);
        if (branchId.HasValue)
            query = query.Where(invoice => invoice.Order.BranchId == branchId.Value);

        if (startDate.HasValue)
        {
            query = query.Where(i => i.IssuedAt >= startDate.Value.Date);
        }

        if (endDate.HasValue)
        {
            var endOfDay = endDate.Value.Date.AddDays(1);
            query = query.Where(i => i.IssuedAt < endOfDay);
        }

        var totalCount = await query.CountAsync();

        var invoices = await query
            .OrderByDescending(i => i.IssuedAt)
            .ThenByDescending(i => i.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(i => new
            {
                i.Id,
                i.InvoiceNumber,
                i.InvoiceStatusId,
                StatusCode = i.InvoiceStatus.Code,
                StatusName = i.InvoiceStatus.Name,
                i.IssuedAt,
                i.TotalAmount,
                PaidAmount = _db.Payments
                    .Where(p => p.InvoiceId == i.Id)
                    // Summed in SQL, converted to base currency (one query, no per-row round trip).
                    .Sum(p => (decimal?)Math.Round(p.Amount / p.Currency.ExchangeRate, 2)) ?? 0m,
                OrderNumber = i.Order != null ? i.Order.OrderNumber : null,
            })
            .ToListAsync();

        return new
        {
            totalCount,
            page,
            perPage = PageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)PageSize),
            invoices
        };
    }

    /// <param name="branchScope">Null for users who may see every branch; otherwise only that branch's invoices are found.</param>
    public async Task<object> GetInvoiceByIdAsync(int invoiceId, int? branchScope = null)
    {
        var invoice = await _db.Invoices
            .AsNoTracking()
            // Order lines, invoice lines, payments and credit memos are separate collections: one
            // statement each, instead of one JOIN that multiplies them (~1 s for a 100-line invoice).
            .AsSplitQuery()
            .Include(i => i.InvoiceStatus)
            .Include(i => i.Order)
                .ThenInclude(o => o.Customer)
            .Include(i => i.Order)
                .ThenInclude(o => o.Branch)
            .Include(i => i.Order)
                .ThenInclude(o => o.DeliveryLocation)
            .Include(i => i.Order)
                .ThenInclude(o => o.CreatedByEmployee)
            .Where(i => i.Id == invoiceId && (branchScope == null || i.Order.BranchId == branchScope))
            .Select(i => new
            {
                i.Id,
                i.InvoiceNumber,
                i.InvoiceStatusId,
                StatusCode = i.InvoiceStatus.Code,
                StatusName = i.InvoiceStatus.Name,
                i.IssuedAt,
                i.Subtotal,
                i.SaleAmount,
                i.DiscountAmount,
                i.DiscountTypeId,
                DiscountTypeCode = i.DiscountType.Code,
                i.DiscountValue,
                i.TaxAmount,
                DeliveryChargeAmount = i.DeliveryChargeAmount,
                i.TotalAmount,
                Order = i.Order == null ? null : new
                {
                    i.Order.Id,
                    i.Order.OrderNumber,
                    i.Order.BranchId,
                    BranchName = i.Order.Branch != null ? i.Order.Branch.Name : null,
                    i.Order.CreatedAt,
                    StatusCode = i.Order.OrderStatus.Code,
                    StatusName = i.Order.OrderStatus.Name,
                    CustomerName = i.Order.Customer != null ? i.Order.Customer.Name : null,
                    CustomerPhone = i.Order.Customer != null ? i.Order.Customer.Phone : null,
                    CustomerEmail = i.Order.Customer != null ? i.Order.Customer.Email : null,
                    CustomerAddress = i.Order.Customer != null ? i.Order.Customer.Address : null,
                    CreatedByEmployeeId = i.Order.CreatedByEmployeeId,
                    CreatedByEmployeeName = i.Order.CreatedByEmployee != null ? i.Order.CreatedByEmployee.Name : null,
                    DeliveryLocation = i.Order.DeliveryLocation.LocationName
                    // The order's lines are not repeated here: InvoiceItems carries the same lines with
                    // their prices (D21), and the duplicate list doubled the cost of this query.
                },
                Payments = _db.Payments
                    .Where(p => p.InvoiceId == i.Id)
                    .Include(p => p.Currency)
                    .OrderByDescending(p => p.PaidAt)
                    .Select(p => new
                    {
                        p.Id,
                        p.Amount,
                        p.PaymentMethodId,
                        PaymentMethodCode = p.PaymentMethod.Code,
                        PaymentMethodName = p.PaymentMethod.Name,
                        p.Reference,
                        p.CurrencyId,
                        p.Currency.Symbol,
                        p.PaidAt
                    })
                    .ToList(),
                CreditMemos = _db.CreditMemos
                    .Where(cm => cm.InvoiceId == i.Id)
                    .OrderByDescending(cm => cm.CreatedAt)
                    .Select(cm => new
                    {
                        cm.Id,
                        cm.CreditMemoNumber,
                        cm.InvoiceId,
                        cm.TotalAmount,
                        cm.CreatedAt,
                        cm.IssuedAt,
                        cm.Reason,
                        Items = _db.CreditMemoItems
                            .Where(cmi => cmi.CreditMemoId == cm.Id)
                            .Select(cmi => new
                            {
                                cmi.InvoiceItemId,
                                cmi.Quantity,
                                cmi.RefundAmount,
                                cmi.Reason,
                                ProductName = cmi.ProductVariant.Product.Name,
                                VariantSku = cmi.ProductVariant.SKU,
                                Attributes = cmi.ProductVariant.AttributeValues
                                    .OrderBy(value => value.ProductAttribute.Name)
                                    .Select(value => new
                                    {
                                        Name = value.ProductAttribute.Name,
                                        value.Value
                                    })
                                    .ToList()
                            })
                            .ToList()
                    })
                    .ToList(),
                InvoiceItems = (
                    from ii in _db.InvoiceItems
                    where ii.InvoiceId == i.Id
                    join pv in _db.ProductVariants on ii.ProductVariantId equals pv.Id
                    join p in _db.Products on pv.ProductId equals p.Id
                    join b in _db.Brands on p.BrandId equals b.Id
                    select new
                    {
                        ii.Id,
                        ii.ProductVariantId,
                        ProductName = p.Name,
                        BrandName = b.Name,
                        VariantSku = pv.SKU,
                        Attributes = pv.AttributeValues
                            .OrderBy(value => value.ProductAttribute.Name)
                            .Select(value => new
                            {
                                Name = value.ProductAttribute.Name,
                                value.Value
                            })
                            .ToList(),
                        ii.Quantity,
                        UnitPrice = ii.FinalUnitPrice,
                        TotalPrice = ii.LineNet,
                        BaseUnitPrice = ii.BaseUnitPrice,
                        SalePerUnit = ii.PerUnitSaleAmount,
                        LineGross = ii.LineGross,
                        ReturnedQuantity = _db.CreditMemoItems.Where(cmi => cmi.InvoiceItemId == ii.Id).Sum(cmi => cmi.Quantity)
                    }
                ).ToList(),
                PaidAmount = _db.Payments
                    .Where(p => p.InvoiceId == i.Id)
                    // Summed in SQL, converted to base currency (one query, no per-row round trip).
                    .Sum(p => (decimal?)Math.Round(p.Amount / p.Currency.ExchangeRate, 2)) ?? 0m
            })
            .FirstOrDefaultAsync();

        if (invoice == null)
            throw new KeyNotFoundException($"Invoice {invoiceId} not found.");

        return invoice;
    }

    public async Task<object> GetInvoiceStatusesAsync()
    {
        return (await _lookups.ListAsync<InvoiceStatus>())
            .Select(s => new { s.Id, s.Code, s.Name, s.Tone });
    }

    /// <summary>One cent of base currency: a remainder this small counts as settled (D19), because a
    /// second-currency cash note cannot match a base-currency total to the cent.</summary>
    private const decimal SettlementTolerance = 0.01m;

    public async Task<PaymentResult> AddPaymentAsync(
        int invoiceId,
        RecordPaymentRequest recordPaymentDto)
    {
        if (recordPaymentDto.Amount < 0.01m || recordPaymentDto.Amount != Pricing.Round2(recordPaymentDto.Amount))
            throw new ArgumentException("Payment amount must be at least 0.01 and have at most two decimals.");
        if (recordPaymentDto.Tendered is decimal tendered && tendered < recordPaymentDto.Amount)
            throw new ArgumentException("Tendered amount cannot be less than the payment amount.");

        var paymentCurrency = await _db.Currencies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == recordPaymentDto.CurrencyId && c.IsActive)
            ?? throw new ArgumentException("Invalid or inactive currency.");

        if (await _lookups.FindAsync<PaymentMethod>(recordPaymentDto.PaymentMethodId) == null)
            throw new ArgumentException($"Payment method {recordPaymentDto.PaymentMethodId} does not exist.");

        // Serializable + retry: two tills paying the same invoice at once can never both succeed.
        return await SerializableTransactions.RunAsync(_db, async () =>
        {
            var invoice = await _db.Invoices
                .Include(i => i.InvoiceStatus)
                .FirstOrDefaultAsync(i => i.Id == invoiceId)
                ?? throw new KeyNotFoundException($"Invoice {invoiceId} not found.");

            if (invoice.InvoiceStatus.Code is not (InvoiceStatusCodes.Issued or InvoiceStatusCodes.PartiallyPaid))
                throw new ArgumentException($"Invoice {invoice.InvoiceNumber} is {invoice.InvoiceStatus.Name.ToLowerInvariant()} and cannot take payments.");

            var existingPayments = await _db.Payments
                .Include(p => p.Currency)
                .Where(p => p.InvoiceId == invoiceId)
                .ToListAsync();

            var paidAmountBase = existingPayments.Sum(p => ConvertToBase(p.Amount, p.Currency.ExchangeRate));
            var incomingAmountBase = ConvertToBase(recordPaymentDto.Amount, paymentCurrency.ExchangeRate);
            var remainingBase = invoice.TotalAmount - paidAmountBase;

            if (remainingBase <= 0m)
                throw new ArgumentException($"Invoice {invoice.InvoiceNumber} is already paid.");
            if (incomingAmountBase > remainingBase + SettlementTolerance)
                throw new ArgumentException($"Payment amount is greater than the remaining amount ({remainingBase:0.00}).");

            _db.Payments.Add(new Payment
            {
                InvoiceId = invoiceId,
                Amount = recordPaymentDto.Amount,
                PaymentMethodId = recordPaymentDto.PaymentMethodId,
                Reference = recordPaymentDto.Reference,
                CurrencyId = recordPaymentDto.CurrencyId,
                PaidAt = DateTime.UtcNow
            });

            var newPaidTotalBase = paidAmountBase + incomingAmountBase;
            var fullyPaid = invoice.TotalAmount - newPaidTotalBase <= SettlementTolerance;
            invoice.InvoiceStatusId = await _lookups.IdAsync<InvoiceStatus>(
                fullyPaid ? InvoiceStatusCodes.FullyPaid : InvoiceStatusCodes.PartiallyPaid);

            var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == invoice.OrderId);
            if (order != null)
            {
                order.OrderStatusId = await _lookups.IdAsync<OrderStatus>(
                    fullyPaid ? OrderStatusCodes.FullyPaid : OrderStatusCodes.PartiallyPaid);
            }

            await _db.SaveChangesAsync();

            return new PaymentResult(
                invoiceId,
                invoice.InvoiceNumber,
                newPaidTotalBase,
                Math.Max(0m, Pricing.Round2(invoice.TotalAmount - newPaidTotalBase)),
                fullyPaid,
                // Change is in the payment's own currency (cash handed back at the till).
                Pricing.Round2((recordPaymentDto.Tendered ?? recordPaymentDto.Amount) - recordPaymentDto.Amount));
        });

        static decimal ConvertToBase(decimal amount, decimal rate)
        {
            if (rate <= 0) throw new InvalidOperationException("Exchange rate must be greater than zero.");
            return Pricing.Round2(amount / rate);
        }
    }

    /// <summary>
    /// Records a return (credit memo) on a paid invoice and puts the units back into the lots they came
    /// from. Each line refunds its net minus its share of the order discount plus its share of the tax;
    /// the delivery fee is not refunded; total refunds never exceed what was paid. All checks and writes
    /// run in one serializable transaction, so a double submission cannot refund or restock twice.
    /// </summary>
    public async Task<object> ReturnInvoiceAsync(
        int invoiceId,
        CreateInvoiceReturnRequest req)
    {
        if (req == null || req.Items == null || req.Items.Count == 0)
            throw new ArgumentException("No items to return.");
        if (req.Items.Any(item => item.Quantity <= 0))
            throw new ArgumentException("Return quantity must be > 0.");

        return await SerializableTransactions.RunAsync(_db, async () =>
        {
            var invoice = await _db.Invoices
                .Include(i => i.Order)
                .Include(i => i.InvoiceStatus)
                .SingleOrDefaultAsync(i => i.Id == invoiceId)
                ?? throw new KeyNotFoundException($"Invoice {invoiceId} not found.");
            if (invoice.InvoiceStatus.Code == InvoiceStatusCodes.Void)
                throw new ArgumentException("Invoice is void and cannot be returned.");

            // D18: a return refunds money, so there must be money to refund; an unpaid order is cancelled instead.
            var paidBase = await _db.Payments
                .Where(p => p.InvoiceId == invoiceId)
                .SumAsync(p => (decimal?)(p.Amount / p.Currency.ExchangeRate)) ?? 0m;
            if (paidBase <= 0m)
                throw new ArgumentException("Nothing has been paid on this invoice; cancel the order instead of returning it.");

            var allLines = await _db.InvoiceItems
                .Include(l => l.CreditMemoItems)
                .Where(l => l.InvoiceId == invoiceId)
                .ToListAsync();
            var requested = req.Items
                .GroupBy(item => item.InvoiceItemId)
                .Select(group => new { LineId = group.Key, Quantity = group.Sum(item => item.Quantity), Reason = group.First().Reason })
                .ToList();
            if (requested.Any(item => allLines.All(line => line.Id != item.LineId)))
                throw new ArgumentException("One or more invoice lines not found for this invoice.");

            var invoiceNet = invoice.Subtotal - invoice.SaleAmount;
            var returns = new List<(InvoiceItem line, int qty, decimal retGross, decimal retSale, decimal retNet, decimal refund, string reason)>();
            foreach (var item in requested)
            {
                var line = allLines.Single(l => l.Id == item.LineId);
                var allowed = line.Quantity - line.CreditMemoItems.Sum(c => c.Quantity);
                if (allowed <= 0)
                    throw new ArgumentException($"Invoice item {line.Id} has no remaining returnable quantity.");
                if (item.Quantity > allowed)
                    throw new ArgumentException($"Invoice item {line.Id} can return at most {allowed}.");

                var ratio = (decimal)item.Quantity / line.Quantity;
                var retNet = R2(line.LineNet * ratio);
                var refund = Pricing.RefundFor(retNet, invoiceNet, invoice.DiscountAmount, invoice.TaxAmount);
                returns.Add((line, item.Quantity, R2(line.LineGross * ratio), R2(line.LineSale * ratio), retNet, refund, item.Reason ?? string.Empty));
            }

            var refundable = invoice.TotalAmount - invoice.DeliveryChargeAmount;
            var refundedBefore = await _db.CreditMemos
                .Where(c => c.InvoiceId == invoiceId)
                .SumAsync(c => (decimal?)c.TotalAmount) ?? 0m;

            // The return that brings every unit back settles to the exact cent of what was charged.
            var returnsEverything = allLines.All(line =>
                line.Quantity - line.CreditMemoItems.Sum(c => c.Quantity) ==
                returns.Where(r => r.line.Id == line.Id).Sum(r => r.qty));
            if (returnsEverything)
            {
                var last = returns[^1];
                var others = returns.Take(returns.Count - 1).Sum(r => r.refund);
                returns[^1] = last with { refund = R2(refundable - refundedBefore - others) };
            }

            var creditTotal = R2(returns.Sum(r => r.refund));
            if (refundedBefore + creditTotal > R2(paidBase) + 0.01m)
                throw new ArgumentException(
                    $"Refund {creditTotal:0.00} would exceed what was paid ({R2(paidBase):0.00}, already refunded {refundedBefore:0.00}).");

            var creditMemo = new CreditMemo
            {
                CreditMemoNumber = await _documentNumbers.NextAsync(DocumentPrefixes.CreditMemo),
                InvoiceId = invoice.Id,
                Reason = req.Reason ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                IssuedAt = DateTime.UtcNow,
                TotalAmount = creditTotal
            };
            _db.CreditMemos.Add(creditMemo);
            await _db.SaveChangesAsync();

            var returnMovementTypeId = await _lookups.IdAsync<MovementType>(MovementTypeCodes.ReturnFromClient);
            foreach (var r in returns)
            {
                _db.CreditMemoItems.Add(new CreditMemoItem
                {
                    CreditMemoId = creditMemo.Id,
                    InvoiceItemId = r.line.Id,
                    ProductVariantId = r.line.ProductVariantId,
                    Quantity = r.qty,
                    LineGross = r.retGross,
                    LineSale = r.retSale,
                    LineNet = r.retNet,
                    RefundAmount = r.refund,
                    Reason = r.reason
                });

                var returnedLots = await _inventory.ReturnSoldQuantityAsync(
                    invoice.OrderId, r.line.ProductVariantId, invoice.Order.BranchId, r.qty);
                foreach (var returnedLot in returnedLots)
                {
                    _db.StockMovements.Add(new StockMovement
                    {
                        ProductVariantId = r.line.ProductVariantId,
                        InventoryLotId = returnedLot.Key,
                        BranchId = invoice.Order.BranchId,
                        MovementTypeId = returnMovementTypeId,
                        Quantity = returnedLot.Value,
                        CreatedAt = DateTime.UtcNow,
                        Reason = $"Credit memo {creditMemo.CreditMemoNumber}"
                    });
                }
            }

            var isFullRefund = returnsEverything || refundedBefore + creditTotal >= refundable - 0.01m;
            invoice.InvoiceStatusId = await _lookups.IdAsync<InvoiceStatus>(
                isFullRefund ? InvoiceStatusCodes.Refunded : InvoiceStatusCodes.PartiallyRefunded);
            invoice.Order.OrderStatusId = await _lookups.IdAsync<OrderStatus>(
                isFullRefund ? OrderStatusCodes.Returned : OrderStatusCodes.PartiallyReturned);

            await _db.SaveChangesAsync();

            return (object)new
            {
                creditMemoId = creditMemo.Id,
                totalRefund = creditMemo.TotalAmount,
                invoiceStatusId = invoice.InvoiceStatusId,
                orderStatusId = invoice.Order.OrderStatusId,
                items = returns.Select(r => new { invoiceItemId = r.line.Id, qty = r.qty })
            };
        });
    }
}
