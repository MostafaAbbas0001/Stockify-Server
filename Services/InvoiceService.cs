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
                    .Include(p => p.Currency)
                    .AsEnumerable()
                    .Sum(p => p.Currency != null && p.Currency.ExchangeRate > 0 
                        ? p.Amount / p.Currency.ExchangeRate 
                        : p.Amount),
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

    public async Task<object> GetInvoiceByIdAsync(int invoiceId)
    {
        var invoice = await _db.Invoices
            .AsNoTracking()
            .Include(i => i.InvoiceStatus)
            .Include(i => i.Order)
                .ThenInclude(o => o.Customer)
            .Include(i => i.Order)
                .ThenInclude(o => o.Branch)
            .Include(i => i.Order)
                .ThenInclude(o => o.DeliveryLocation)
            .Include(i => i.Order)
                .ThenInclude(o => o.CreatedByEmployee)
            .Where(i => i.Id == invoiceId)
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
                    DeliveryLocation = i.Order.DeliveryLocation.LocationName,
                    OrderItems = _db.OrderItems
                        .Where(oi => oi.OrderId == i.Order.Id)
                        .Select(oi => new
                        {
                            oi.Id,
                            oi.ProductVariantId,
                            ProductName = oi.ProductVariant.Product.Name,
                            BrandName = oi.ProductVariant.Product.Brand.Name,
                            VariantSku = oi.ProductVariant.SKU,
                            CategoryName = oi.ProductVariant.Product.Category.Name,
                            Attributes = oi.ProductVariant.AttributeValues
                                .OrderBy(value => value.ProductAttribute.Name)
                                .Select(value => new
                                {
                                    Name = value.ProductAttribute.Name,
                                    value.Value
                                })
                                .ToList(),
                            oi.Quantity
                        })
                        .ToList()
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
                    .Include(p => p.Currency)
                    .AsEnumerable()
                    .Sum(p => p.Currency != null && p.Currency.ExchangeRate > 0 
                        ? p.Amount / p.Currency.ExchangeRate 
                        : p.Amount)
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

    public async Task<object> AddPaymentAsync(
        int invoiceId,
        RecordPaymentRequest recordPaymentDto)
    {
        var invoice = await _db.Invoices
          .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null)
          throw new KeyNotFoundException($"Invoice {invoiceId} not found.");

        // Validate currency and convert amounts to base currency using ExchangeRate
        var paymentCurrency = await _db.Currencies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == recordPaymentDto.CurrencyId && c.IsActive);

        if (paymentCurrency == null)
            throw new ArgumentException("Invalid or inactive currency.");

        if (await _lookups.FindAsync<PaymentMethod>(recordPaymentDto.PaymentMethodId) == null)
            throw new ArgumentException($"Payment method {recordPaymentDto.PaymentMethodId} does not exist.");

        decimal ConvertToBase(decimal amount, decimal rate)
        {
            if (rate <= 0) throw new InvalidOperationException("Exchange rate must be greater than zero.");
            return Math.Round(amount / rate, 2, MidpointRounding.AwayFromZero);
        }

        var existingPayments = await _db.Payments
            .Include(p => p.Currency)
            .Where(p => p.InvoiceId == invoiceId)
            .ToListAsync();

        var paidAmountBase = existingPayments.Sum(p => ConvertToBase(p.Amount, p.Currency.ExchangeRate));
        var incomingAmountBase = ConvertToBase(recordPaymentDto.Amount, paymentCurrency.ExchangeRate);

        if (invoice.TotalAmount <= paidAmountBase)
            throw new ArgumentException($"Invoice {invoiceId} is already paid.");

        if (incomingAmountBase > invoice.TotalAmount - paidAmountBase)
            throw new ArgumentException($"Payment amount is greater than the remaining amount.");

        var payment = new Payment
        {
          InvoiceId = invoiceId,
          Amount = recordPaymentDto.Amount,
          PaymentMethodId = recordPaymentDto.PaymentMethodId,
          Reference = recordPaymentDto.Reference,
          CurrencyId = recordPaymentDto.CurrencyId,
          PaidAt = DateTime.UtcNow
        };

        await _db.Payments.AddAsync(payment);

        var newPaidTotalBase = paidAmountBase + incomingAmountBase;
        var fullyPaid = newPaidTotalBase >= invoice.TotalAmount;
        invoice.InvoiceStatusId = await _lookups.IdAsync<InvoiceStatus>(
            fullyPaid ? InvoiceStatusCodes.FullyPaid : InvoiceStatusCodes.PartiallyPaid);

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == invoice.OrderId);
        if (order != null)
        {
          order.OrderStatusId = await _lookups.IdAsync<OrderStatus>(
            fullyPaid ? OrderStatusCodes.FullyPaid : OrderStatusCodes.PartiallyPaid);
        }

        await _db.SaveChangesAsync();

        return new
        {
          invoiceId,
          invoice.InvoiceNumber,
          paidAmountBase = newPaidTotalBase
        };
    }

    public async Task<object> ReturnInvoiceAsync(
        int invoiceId,
        CreateInvoiceReturnRequest req)
    {
        if (req == null || req.Items == null || req.Items.Count == 0)
            throw new ArgumentException("No items to return.");

        var invoice = await _db.Invoices
            .Include(i => i.Order)
            .SingleOrDefaultAsync(i => i.Id == invoiceId);
        if (invoice == null)
            throw new KeyNotFoundException($"Invoice {invoiceId} not found.");
        if (invoice.InvoiceStatusId == await _lookups.IdAsync<InvoiceStatus>(InvoiceStatusCodes.Void))
            throw new ArgumentException("Invoice is void and cannot be returned.");

        var lineIds = req.Items.Select(x => x.InvoiceItemId).Distinct().ToList();
        var lines = await _db.InvoiceItems
            .Include(l => l.CreditMemoItems)
            .Where(l => l.InvoiceId == invoiceId && lineIds.Contains(l.Id))
            .ToListAsync();
        if (lines.Count != lineIds.Count)
            throw new ArgumentException("One or more invoice lines not found for this invoice.");

        using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var computed = new List<(InvoiceItem line, int qty, decimal retGross, decimal retSale, decimal retNet, string reason)>();

        foreach (var item in req.Items)
        {
            if (item.Quantity <= 0)
                throw new ArgumentException("Return quantity must be > 0.");

            var line = lines.First(l => l.Id == item.InvoiceItemId);
            var alreadyReturned = line.CreditMemoItems.Sum(c => c.Quantity);
            var allowed = line.Quantity - alreadyReturned;
            if (allowed <= 0)
                throw new ArgumentException($"Invoice item {line.Id} has no remaining returnable quantity.");
            if (item.Quantity > allowed)
                throw new ArgumentException($"Invoice item {line.Id} can return at most {allowed}.");

            var ratio = (decimal)item.Quantity / (decimal)line.Quantity;
            var retGross = R2(line.LineGross * ratio);
            var retSale = R2(line.LineSale * ratio);
            var retNet = R2(line.LineNet * ratio);
            computed.Add((line, item.Quantity, retGross, retSale, retNet, item.Reason ?? string.Empty));
        }

        var creditSubtotal = R2(computed.Sum(x => x.retGross));
        var creditSale = R2(computed.Sum(x => x.retSale));
        var discShare = invoice.Subtotal > 0m ? (creditSubtotal / invoice.Subtotal) : 0m;
        var creditExtraDiscount = R2(invoice.DiscountAmount * discShare);
        var creditTax = R2(invoice.TaxAmount * discShare);
        var deliveryRefund = 0m;
        var creditTotal = R2(creditSubtotal + creditTax + deliveryRefund - creditSale - creditExtraDiscount);

        var cm = new CreditMemo
        {
            CreditMemoNumber = await _documentNumbers.NextAsync(DocumentPrefixes.CreditMemo),
            InvoiceId = invoice.Id,
            CustomerId = null,
            Reason = req.Reason ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            IssuedAt = DateTime.UtcNow,
            TotalAmount = creditTotal
        };
        _db.CreditMemos.Add(cm);
        await _db.SaveChangesAsync();

        decimal runningSum = 0m;
        for (int i = 0; i < computed.Count; i++)
        {
            var c = computed[i];
            var lineDiscShare = invoice.Subtotal > 0m ? (c.retGross / invoice.Subtotal) : 0m;
            var lineExtra = R2(invoice.DiscountAmount * lineDiscShare);
            var lineTax = R2(invoice.TaxAmount * lineDiscShare);
            var refund = R2(c.retGross + lineTax - c.retSale - lineExtra);

            if (i == computed.Count - 1)
            {
                var remaining = creditTotal - runningSum;
                refund = remaining;
            }
            runningSum += refund;

            _db.CreditMemoItems.Add(new CreditMemoItem
            {
                CreditMemoId = cm.Id,
                InvoiceItemId = c.line.Id,
                ProductVariantId = c.line.ProductVariantId,
                Quantity = c.qty,
                LineGross = c.retGross,
                LineSale = c.retSale,
                LineNet = c.retNet,
                RefundAmount = refund,
                Reason = c.reason
            });

            var returnedLots = await _inventory.ReturnSoldQuantityAsync(
                invoice.OrderId,
                c.line.ProductVariantId,
                invoice.Order.BranchId,
                c.qty);

            foreach (var returnedLot in returnedLots)
            {
                _db.StockMovements.Add(new StockMovement
                {
                    ProductVariantId = c.line.ProductVariantId,
                    InventoryLotId = returnedLot.Key,
                    BranchId = invoice.Order.BranchId,
                    MovementTypeId = await _lookups.IdAsync<MovementType>(MovementTypeCodes.ReturnFromClient),
                    Quantity = returnedLot.Value,
                    CreatedAt = DateTime.UtcNow,
                    Reason = "Credit Memo Return"
                });
            }
        }

        await _db.SaveChangesAsync();

        var totalRefunded = await _db.CreditMemos
            .Where(x => x.InvoiceId == invoice.Id)
            .Select(x => (decimal?)x.TotalAmount)
            .SumAsync() ?? 0m;

        const decimal EPS = 0.01m;
        var refundableAmount = invoice.TotalAmount - invoice.DeliveryChargeAmount;
        var isFullRefund = Math.Abs(totalRefunded - refundableAmount) <= EPS || totalRefunded >= (refundableAmount - EPS);

        invoice.InvoiceStatusId = await _lookups.IdAsync<InvoiceStatus>(
            isFullRefund ? InvoiceStatusCodes.Refunded : InvoiceStatusCodes.PartiallyRefunded);

        var order = await _db.Orders.SingleAsync(o => o.Id == invoice.OrderId);
        order.OrderStatusId = await _lookups.IdAsync<OrderStatus>(
            isFullRefund ? OrderStatusCodes.Returned : OrderStatusCodes.PartiallyReturned);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return new
        {
            creditMemoId = cm.Id,
            totalRefund = cm.TotalAmount,
            invoiceStatusId = invoice.InvoiceStatusId,
            orderStatusId = order.OrderStatusId,
            items = computed.Select(x => new { invoiceItemId = x.line.Id, qty = x.qty })
        };
    }
}
