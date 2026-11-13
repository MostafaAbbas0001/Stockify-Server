namespace Stockify.Services;

public class OrderService
{
    private const int PageSize = 20;
    private readonly AppDbContext _db;
    private readonly InventoryLotService _inventory;
    private readonly DocumentNumberService _documentNumbers;
    private readonly LookupService _lookups;

    public OrderService(
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

    /// <summary>
    /// Status changes the UI may offer for an order in the given status. Cancel is offered only
    /// before any money is taken (D18); a paid or part-paid sale is reversed with a return.
    /// </summary>
    public static string[] AllowedActionsFor(string statusCode) => statusCode switch
    {
        OrderStatusCodes.Draft => new[] { "ship", "cancel" },
        OrderStatusCodes.InTransit => new[] { "deliver", "cancel" },
        OrderStatusCodes.Delivered => new[] { "cancel" },
        _ => Array.Empty<string>()
    };

    /// <summary>
    /// Places an order at the given branch: validates and prices it (Pricing), then in one
    /// serializable transaction re-checks stock, writes the order, takes the stock FEFO and issues
    /// the invoice. Joins the caller's transaction when there is one (checkout).
    /// </summary>
    public async Task<PlacedOrder> PlaceOrderAsync(CreateOrderRequest dto, int branchId)
    {
        var sale = await PrepareSaleAsync(dto, branchId);

        // Serializable + retry: two tills selling the last unit cannot both succeed, and the loser gets a
        // clean "Insufficient stock" on the retry.
        return await SerializableTransactions.RunAsync(_db, async () =>
        {
            await EnsureStockAsync(branchId, sale.Lines);
            var order = await CreateOrderAsync(dto, branchId, sale);
            await TakeStockAsync(order);
            var invoice = await CreateInvoiceAsync(order, sale, dto.DiscountValue);
            return new PlacedOrder(order.Id, invoice.Id, invoice.InvoiceNumber, invoice.TotalAmount);
        });
    }

    /// <summary>Everything an order needs that is read before the transaction starts.</summary>
    private sealed record PreparedSale(
        DeliveryLocation Location,
        int TaxRateId,
        DiscountType DiscountType,
        IReadOnlyList<Pricing.Line> Lines,
        Pricing.Totals Totals);

    /// <summary>Validates every id in the request (tenant-filtered) and prices the cart.</summary>
    private async Task<PreparedSale> PrepareSaleAsync(CreateOrderRequest dto, int branchId)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            throw new ArgumentException("Order must have at least one item.");

        if (await _db.Customers.FindAsync(dto.CustomerId) == null)
            throw new ArgumentException($"Customer with ID {dto.CustomerId} not found.");

        var location = await _db.DeliveryLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == dto.DeliveryLocationId)
            ?? throw new ArgumentException($"No delivery fee found for DeliveryChargeId={dto.DeliveryLocationId}.");

        if (await _db.Employees.FindAsync(dto.CreatedByEmployeeId) == null)
            throw new ArgumentException($"Employee with ID {dto.CreatedByEmployeeId} not found.");

        var taxRate = await _db.TaxRates.FirstOrDefaultAsync(t => t.IsDefault)
            ?? throw new KeyNotFoundException("No default tax rate is configured for this company.");

        var itemGroups = dto.Items
            .GroupBy(i => i.ProductVariantId)
            .Select(g => new { VariantId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();
        if (itemGroups.Any(g => g.Quantity <= 0))
            throw new ArgumentException("Each item quantity must be > 0.");

        var variantIds = itemGroups.Select(g => g.VariantId).ToList();
        var variants = await _db.ProductVariants
            .Include(v => v.Sale)
                .ThenInclude(sale => sale!.SaleType)
            .Where(v => variantIds.Contains(v.Id))
            .ToListAsync();
        if (variants.Count != variantIds.Count)
        {
            var missing = string.Join(", ", variantIds.Except(variants.Select(v => v.Id)));
            throw new ArgumentException($"One or more variants not found: {missing}");
        }

        var discountType = dto.DiscountTypeId is int discountTypeId
            ? await _lookups.FindAsync<DiscountType>(discountTypeId)
                ?? throw new ArgumentException($"Discount type {discountTypeId} does not exist.")
            : (await _lookups.ListAsync<DiscountType>()).First(type => type.Code == DiscountTypeCodes.None);

        // Every amount comes from Pricing so the POS preview and the invoice always agree.
        var now = DateTime.UtcNow;
        var lines = itemGroups.Select(g =>
        {
            var variant = variants.First(x => x.Id == g.VariantId);
            return new Pricing.Line(g.VariantId, g.Quantity, variant.Price, Pricing.PerUnitSaleAmount(variant.Price, variant.Sale, now));
        }).ToList();
        var totals = Pricing.OrderTotals(lines, discountType.Code, dto.DiscountValue, taxRate.Rate, location.Fee);

        // Fail fast before opening the transaction; EnsureStockAsync re-checks inside it.
        await EnsureStockAsync(branchId, lines);

        return new PreparedSale(location, taxRate.Id, discountType, lines, totals);
    }

    private async Task EnsureStockAsync(int branchId, IReadOnlyList<Pricing.Line> lines)
    {
        var available = await _inventory.GetAvailableQuantitiesAsync(branchId, lines.Select(l => l.VariantId).ToList());
        if (lines.Any(line => available.GetValueOrDefault(line.VariantId) < line.Quantity))
            throw new ArgumentException("Insufficient stock.");
    }

    /// <summary>Writes the order (DRAFT; payment is recorded separately) and its items.</summary>
    private async Task<Order> CreateOrderAsync(CreateOrderRequest dto, int branchId, PreparedSale sale)
    {
        var order = new Order
        {
            OrderNumber = await _documentNumbers.NextAsync(DocumentPrefixes.Order),
            OrderStatusId = await _lookups.IdAsync<OrderStatus>(OrderStatusCodes.Draft),
            OrderTypeId = await _lookups.IdAsync<OrderType>(
                sale.Location.IsInStore ? OrderTypeCodes.Pos : OrderTypeCodes.Delivery),
            CustomerId = dto.CustomerId,
            DeliveryLocationId = sale.Location.Id,
            CreatedByEmployeeId = dto.CreatedByEmployeeId,
            CreatedAt = DateTime.UtcNow,
            BranchId = branchId,
            OrderItems = sale.Lines.Select(line => new OrderItem
            {
                ProductVariantId = line.VariantId,
                Quantity = line.Quantity
            }).ToList()
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    /// <summary>Takes each item's stock FEFO and remembers exactly which lots supplied it.</summary>
    private async Task TakeStockAsync(Order order)
    {
        var saleMovementTypeId = await _lookups.IdAsync<MovementType>(MovementTypeCodes.Sale);
        foreach (var orderItem in order.OrderItems)
        {
            var allocations = await _inventory.DeductAvailableQuantityAsync(
                order.BranchId,
                orderItem.ProductVariantId,
                orderItem.Quantity);

            foreach (var allocation in allocations)
            {
                _db.OrderItemLots.Add(new OrderItemLot
                {
                    OrderItemId = orderItem.Id,
                    InventoryLotId = allocation.Key,
                    Quantity = allocation.Value
                });

                _db.StockMovements.Add(new StockMovement
                {
                    ProductVariantId = orderItem.ProductVariantId,
                    InventoryLotId = allocation.Key,
                    BranchId = order.BranchId,
                    MovementTypeId = saleMovementTypeId,
                    Quantity = -allocation.Value,
                    CreatedAt = DateTime.UtcNow,
                    Reason = $"Order #{order.Id}"
                });
            }
        }
    }

    /// <summary>Issues the invoice with a price snapshot of every line.</summary>
    private async Task<Invoice> CreateInvoiceAsync(Order order, PreparedSale sale, decimal discountValue)
    {
        var invoice = new Invoice
        {
            InvoiceNumber = await _documentNumbers.NextAsync(DocumentPrefixes.Invoice),
            OrderId = order.Id,
            IssuedAt = DateTime.UtcNow,
            TaxRateId = sale.TaxRateId,
            DeliveryChargeAmount = sale.Totals.DeliveryFee,
            Subtotal = sale.Totals.GrossSubtotal,   // before any discounts
            SaleAmount = sale.Totals.SaleAmount,    // item-level promotions
            DiscountTypeId = sale.DiscountType.Id,
            DiscountValue = discountValue,
            DiscountAmount = sale.Totals.DiscountAmount,
            TaxAmount = sale.Totals.TaxAmount,
            TotalAmount = sale.Totals.Total,
            // A sale that costs nothing (free items, 100 % discount) is settled the moment it is made.
            InvoiceStatusId = await _lookups.IdAsync<InvoiceStatus>(
                sale.Totals.Total == 0m ? InvoiceStatusCodes.FullyPaid : InvoiceStatusCodes.Issued),
            InvoiceItems = sale.Lines.Select(line => new InvoiceItem
            {
                ProductVariantId = line.VariantId,
                Quantity = line.Quantity,
                BaseUnitPrice = line.BaseUnitPrice,
                PerUnitSaleAmount = line.PerUnitSaleAmount,
                FinalUnitPrice = line.FinalUnitPrice,
                LineGross = line.LineGross,
                LineSale = line.LineSale,
                LineNet = line.LineNet
            }).ToList()
        };

        _db.Invoices.Add(invoice);
        if (sale.Totals.Total == 0m)
            order.OrderStatusId = await _lookups.IdAsync<OrderStatus>(OrderStatusCodes.FullyPaid);
        await _db.SaveChangesAsync();
        return invoice;
    }

    public async Task<object> UpdateOrderStatusAsync(
        int orderId,
        UpdateOrderStatusRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Action))
            throw new ArgumentException("Action is required (ship or deliver).");

        var action = request.Action.Trim().ToLower();

        if (action == "cancel")
            throw new ArgumentException("Cancelling is a separate action: POST /api/orders/{id}/cancel (needs order.cancel).");
        if (action != "ship" && action != "deliver")
            throw new ArgumentException("Invalid action. Allowed: ship, deliver.");

        var order = await _db.Orders
            .Include(o => o.OrderStatus)
            .SingleOrDefaultAsync(o => o.Id == orderId)
            ?? throw new KeyNotFoundException($"Order {orderId} not found.");

        var status = order.OrderStatus.Code;
        if (status == OrderStatusCodes.Cancelled)
            throw new ArgumentException("Order is cancelled and cannot be updated.");

        if (action == "ship")
        {
            if (status == OrderStatusCodes.FullyPaid)
                throw new ArgumentException("Order is already paid.");

            if (order.ShippedAt != null || status == OrderStatusCodes.InTransit)
                return new
                {
                    order.Id,
                    order.OrderStatusId,
                    order.ShippedAt
                };

            order.ShippedAt = DateTime.UtcNow;
            order.OrderStatusId = await _lookups.IdAsync<OrderStatus>(OrderStatusCodes.InTransit);

            await _db.SaveChangesAsync();
            return new
            {
                order.Id,
                order.OrderStatusId,
                order.ShippedAt
            };
        }

        if (status == OrderStatusCodes.Delivered)
            return new
            {
                order.Id,
                order.OrderStatusId
            };

        if (status != OrderStatusCodes.InTransit)
            throw new ArgumentException("Only in-transit orders can be marked as delivered.");

        order.OrderStatusId = await _lookups.IdAsync<OrderStatus>(OrderStatusCodes.Delivered);
        await _db.SaveChangesAsync();
        return new
        {
            order.Id,
            order.OrderStatusId
        };
    }

    /// <summary>
    /// Cancels an order nobody has paid for or returned against (D18): puts the units still out back
    /// into their lots and voids the invoice. Money already taken is reversed through a return instead.
    /// </summary>
    public Task<object> CancelOrderAsync(int orderId, string? reason) =>
        SerializableTransactions.RunAsync(_db, () => CancelInTransactionAsync(orderId, reason));

    private async Task<object> CancelInTransactionAsync(int orderId, string? reason)
    {
        var order = await _db.Orders
            .Include(o => o.OrderStatus)
            .Include(o => o.OrderItems).ThenInclude(item => item.Lots)
            .SingleOrDefaultAsync(o => o.Id == orderId)
            ?? throw new KeyNotFoundException($"Order {orderId} not found.");

        var status = order.OrderStatus.Code;
        if (status == OrderStatusCodes.Cancelled)
            throw new ArgumentException("Order is already cancelled.");
        if (status is OrderStatusCodes.Returned or OrderStatusCodes.PartiallyReturned)
            throw new InvalidOperationException("This order has returns; it cannot be cancelled.");
        if (await _db.Payments.AnyAsync(p => p.Invoice.OrderId == order.Id))
            throw new InvalidOperationException("This order has payments; refund it through a return instead of cancelling.");

        var returnMovementTypeId = await _lookups.IdAsync<MovementType>(MovementTypeCodes.ReturnFromClient);
        var reasonText = string.IsNullOrWhiteSpace(reason) ? "cancelled" : $"cancelled: {reason.Trim()}";
        foreach (var item in order.OrderItems)
        {
            // Only the units still out with the customer come back.
            var outstanding = item.Quantity - item.Lots.Sum(lot => lot.ReturnedQuantity);
            if (outstanding <= 0)
                continue;

            var returnedLots = await _inventory.ReturnSoldQuantityAsync(
                order.Id,
                item.ProductVariantId,
                order.BranchId,
                outstanding);

            foreach (var returnedLot in returnedLots)
            {
                _db.StockMovements.Add(new StockMovement
                {
                    ProductVariantId = item.ProductVariantId,
                    InventoryLotId = returnedLot.Key,
                    BranchId = order.BranchId,
                    MovementTypeId = returnMovementTypeId,
                    Quantity = returnedLot.Value,
                    CreatedAt = DateTime.UtcNow,
                    Reason = $"Order #{orderId} {reasonText}"
                });
            }
        }

        order.OrderStatusId = await _lookups.IdAsync<OrderStatus>(OrderStatusCodes.Cancelled);
        order.CancelledAt = DateTime.UtcNow;
        order.CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        // void related invoice
        var invoice = await _db.Invoices.SingleOrDefaultAsync(i => i.OrderId == order.Id);
        if (invoice != null)
        {
            invoice.InvoiceStatusId = await _lookups.IdAsync<InvoiceStatus>(InvoiceStatusCodes.Void);
        }

        await _db.SaveChangesAsync();

        return new
        {
            order.Id,
            order.OrderStatusId,
            order.CancelledAt
        };
    }

    public async Task<object> GetOrderStatusesAsync()
    {
        return (await _lookups.ListAsync<OrderStatus>())
            .Select(s => new { s.Id, s.Code, s.Name, s.Tone });
    }

    public async Task<object> GetAllOrdersAsync(
        string? search,
        int? statusId,
        int? customerId,
        int? branchId,
        int? deliveryLocationId,
        DateTime? startDate,
        DateTime? endDate,
        bool forDeliveryOnly,
        int page)
    {
        page = Math.Max(page, 1);
        var query = _db.Orders.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().ToLower()}%";
            query = query.Where(order =>
                EF.Functions.Like(order.OrderNumber.ToLower(), pattern) ||
                (order.Customer != null &&
                 (EF.Functions.Like(order.Customer.Name.ToLower(), pattern) ||
                  EF.Functions.Like(order.Customer.Phone.ToLower(), pattern) ||
                  EF.Functions.Like(order.Customer.Email.ToLower(), pattern))));
        }

        if (statusId.HasValue)
            query = query.Where(o => o.OrderStatusId == statusId.Value);
        if (customerId.HasValue)
            query = query.Where(order => order.CustomerId == customerId.Value);
        if (branchId.HasValue)
            query = query.Where(order => order.BranchId == branchId.Value);
        if (deliveryLocationId.HasValue)
            query = query.Where(order => order.DeliveryLocationId == deliveryLocationId.Value);

        if (forDeliveryOnly)
        {
            query = query.Where(o => !o.DeliveryLocation.IsInStore);
        }

        // Filter by date range
        if (startDate.HasValue)
        {
            query = query.Where(o => o.CreatedAt >= startDate.Value.Date);
        }

        if (endDate.HasValue)
        {
            // Include the entire end date by comparing against start of next day
            var endOfDay = endDate.Value.Date.AddDays(1);
            query = query.Where(o => o.CreatedAt < endOfDay);
        }

        var totalCount = await query.CountAsync();

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                TotalAmount = _db.Invoices
                    .Where(i => i.OrderId == o.Id)
                    .OrderByDescending(i => i.Id)
                    .Select(i => i.TotalAmount)
                    .FirstOrDefault(),
                CustomerName = o.Customer != null ? o.Customer.Name : null,
                StatusId = o.OrderStatusId,
                StatusCode = o.OrderStatus.Code,
                StatusName = o.OrderStatus.Name,
                DeliveryLocation = o.DeliveryLocation.LocationName,
                o.DeliveryLocationId,
                o.CreatedAt,
            })
            .ToListAsync();

        return new
        {
            totalCount,
            page,
            perPage = PageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)PageSize),
            orders
        };
    }

    /// <param name="branchScope">Null for users who may see every branch; otherwise only that branch's orders are found.</param>
    public async Task<object> GetOrderDetailsAsync(int orderId, int? branchScope = null)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId && (branchScope == null || o.BranchId == branchScope))
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                StatusId = o.OrderStatusId,
                StatusCode = o.OrderStatus.Code,
                StatusName = o.OrderStatus.Name,
                AllowedActions = AllowedActionsFor(o.OrderStatus.Code),
                CustomerName = o.Customer != null ? o.Customer.Name : null,
                CustomerPhone = o.Customer != null ? o.Customer.Phone : null,
                CustomerEmail = o.Customer != null ? o.Customer.Email : null,
                CustomerAddress = o.Customer != null ? o.Customer.Address : null,
                DeliveryLocation = o.DeliveryLocation.LocationName,
                o.CreatedAt,
                o.ShippedAt,
                o.CancelledAt,
                o.CancelReason,
                CreatedByEmployeeName = o.CreatedByEmployee != null ? o.CreatedByEmployee.Name : null,
                InvoiceId = _db.Invoices
                    .Where(i => i.OrderId == o.Id)
                    .OrderByDescending(i => i.Id)
                    .Select(i => i.Id)
                    .FirstOrDefault(),

                Subtotal = _db.Invoices
                    .Where(i => i.OrderId == o.Id)
                    .OrderByDescending(i => i.Id)
                    .Select(i => i.Subtotal)
                    .FirstOrDefault(),
                TaxAmount = _db.Invoices
                    .Where(i => i.OrderId == o.Id)
                    .OrderByDescending(i => i.Id)
                    .Select(i => i.TaxAmount)
                    .FirstOrDefault(),
                DeliveryChargeAmount = _db.Invoices
                    .Where(i => i.OrderId == o.Id)
                    .OrderByDescending(i => i.Id)
                    .Select(i => i.DeliveryChargeAmount)
                    .FirstOrDefault(),
                SaleAmount = _db.Invoices
                    .Where(i => i.OrderId == o.Id)
                    .OrderByDescending(i => i.Id)
                    .Select(i => i.SaleAmount)
                    .FirstOrDefault(),
                DiscountAmount = _db.Invoices
                    .Where(i => i.OrderId == o.Id)
                    .OrderByDescending(i => i.Id)
                    .Select(i => i.DiscountAmount)
                    .FirstOrDefault(),
                TotalAmount = _db.Invoices
                    .Where(i => i.OrderId == o.Id)
                    .OrderByDescending(i => i.Id)
                    .Select(i => i.TotalAmount)
                    .FirstOrDefault(),
                PaidAmount = _db.Payments
                    .Where(p => _db.Invoices
                        .Where(i => i.OrderId == o.Id)
                        .OrderByDescending(i => i.Id)
                        .Select(i => i.Id)
                        .FirstOrDefault() == p.InvoiceId)
                    // Summed in SQL, converted to base currency (one query, no per-row round trip).
                    .Sum(p => (decimal?)(p.Amount / p.Currency.ExchangeRate)) ?? 0m,
                Payments = _db.Payments
                    .Where(p => _db.Invoices
                        .Where(i => i.OrderId == o.Id)
                        .OrderByDescending(i => i.Id)
                        .Select(i => i.Id)
                        .FirstOrDefault() == p.InvoiceId)
                    .Include(p => p.Currency)
                    .Select(p => new
                    {
                        p.Amount,
                        p.PaymentMethodId,
                        PaymentMethodCode = p.PaymentMethod.Code,
                        PaymentMethodName = p.PaymentMethod.Name,
                        p.PaidAt,
                        p.CurrencyId,
                        CurrencySymbol = p.Currency != null ? p.Currency.Symbol : null
                    })
                    .ToList(),

                Items = o.OrderItems
                .OrderBy(oi => oi.Id)
                .Select(oi => new
                {
                    OrderItemId = oi.Id,
                    oi.ProductVariantId,
                    SKU = oi.ProductVariant.SKU,
                    ProductName = oi.ProductVariant.Product.Name,
                    Attributes = oi.ProductVariant.AttributeValues
                        .OrderBy(value => value.ProductAttribute.Name)
                        .Select(value => new
                        {
                            Name = value.ProductAttribute.Name,
                            value.Value
                        })
                        .ToList(),
                    Lots = oi.Lots.Select(lot => new
                    {
                        lot.InventoryLotId,
                        lot.InventoryLot.LotNumber,
                        lot.InventoryLot.ExpiryDate,
                        lot.Quantity,
                        lot.ReturnedQuantity
                    }).ToList(),
                    Quantity = oi.Quantity
                })
                .ToList(),
            })
            .FirstOrDefaultAsync();

        if (order == null)
            throw new KeyNotFoundException($"Order with ID {orderId} not found.");

        return order;
    }
}
