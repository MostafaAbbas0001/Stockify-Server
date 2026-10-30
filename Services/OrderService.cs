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

    /// <summary>Status changes the UI may offer for an order in the given status.</summary>
    public static string[] AllowedActionsFor(string statusCode) => statusCode switch
    {
        OrderStatusCodes.Draft => new[] { "ship", "cancel" },
        OrderStatusCodes.InTransit => new[] { "deliver", "cancel" },
        OrderStatusCodes.Delivered or OrderStatusCodes.PartiallyPaid => new[] { "cancel" },
        _ => Array.Empty<string>()
    };

    public async Task<object> PlaceOrderAsync(CreateOrderRequest dto, int branchId)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            throw new ArgumentException("Order must have at least one item.");

        var customer = await _db.Customers.FindAsync(dto.CustomerId);
        if (customer == null)
            throw new ArgumentException($"Customer with ID {dto.CustomerId} not found.");

        var deliveryCharge = await _db.DeliveryLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == dto.DeliveryLocationId);
        if (deliveryCharge == null)
            throw new ArgumentException($"No delivery fee found for DeliveryChargeId={dto.DeliveryLocationId}.");

        // Validate employee exists
        var employee = await _db.Employees.FindAsync(dto.CreatedByEmployeeId);
        if (employee == null)
            throw new ArgumentException($"Employee with ID {dto.CreatedByEmployeeId} not found.");

        var taxRateEntity = await _db.TaxRates.FirstOrDefaultAsync(t => t.IsDefault);
        if (taxRateEntity == null)
            throw new KeyNotFoundException("No default tax rate is configured for this company.");
        var taxRate = taxRateEntity.Rate;

        var itemGroups = dto.Items
            .GroupBy(i => i.ProductVariantId)
            .Select(g => new { VariantId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (itemGroups.Any(g => g.Quantity <= 0))
            throw new ArgumentException("Each item quantity must be > 0.");

        var variantIds = itemGroups.Select(g => g.VariantId).ToList();

        // Include Sale so we can compute prices correctly
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

        // Stock checks from user's branch
        var stockLevels = await _inventory.GetAvailableQuantitiesAsync(branchId, variantIds);

        var shortages = new List<string>();
        foreach (var g in itemGroups)
        {
            var available = stockLevels.GetValueOrDefault(g.VariantId, 0);
            if (available < g.Quantity)
                shortages.Add($"Variant {g.VariantId} has only {available} in stock, requested {g.Quantity}.");
        }
        if (shortages.Count > 0)
            throw new ArgumentException("Insufficient stock.");

        // Price calculations with sales (snapshot for invoice)
        static decimal Round2(decimal x) => Math.Round(x, 2, MidpointRounding.AwayFromZero);

        // Order-level extra discount; no discount type means "none".
        var discountType = dto.DiscountTypeId is int discountTypeId
            ? await _lookups.FindAsync<DiscountType>(discountTypeId)
                ?? throw new ArgumentException($"Discount type {discountTypeId} does not exist.")
            : (await _lookups.ListAsync<DiscountType>()).First(type => type.Code == DiscountTypeCodes.None);

        // helper for extra discount (percent/amount) computed server-side
        static decimal ComputeExtraDiscount(decimal baseAmount, string typeCode, decimal v)
        {
            if (baseAmount <= 0) return 0m;

            if (typeCode == DiscountTypeCodes.Percent)
            {
                if (v < 0) v = 0;
                if (v > 100) v = 100;
                return Round2(baseAmount * (v / 100m));
            }
            if (typeCode == DiscountTypeCodes.Amount)
            {
                if (v < 0) v = 0;
                return Round2(Math.Min(v, baseAmount));
            }
            return 0m;
        }

        var now = DateTime.UtcNow;

        var lineCalcs = itemGroups.Select(g =>
        {
            var v = variants.First(x => x.Id == g.VariantId);
            var basePrice = v.Price;

            // Determine if sale is active
            var s = v.Sale;
            bool isActive =
                s != null &&
                !s.IsDeleted &&
                (!s.StartDate.HasValue || s.StartDate <= now) &&
                (!s.EndDate.HasValue || now <= s.EndDate);

            decimal perUnitSaleAmount = 0m;
            if (isActive && s != null)
            {
                if (s.SaleType.Code == SaleTypeCodes.Percentage)
                    perUnitSaleAmount = Round2(basePrice * s.Value / 100m);
                else // FixedAmount
                    perUnitSaleAmount = Round2(Math.Min(s.Value, basePrice));
            }

            if (perUnitSaleAmount > basePrice) perUnitSaleAmount = basePrice;

            var finalUnitPrice = Round2(basePrice - perUnitSaleAmount);
            var lineGross = Round2(basePrice * g.Quantity);
            var lineSale = Round2(perUnitSaleAmount * g.Quantity);
            var lineNet = Round2(finalUnitPrice * g.Quantity);

            return new
            {
                g.VariantId,
                g.Quantity,
                BaseUnitPrice = basePrice,
                PerUnitSaleAmount = perUnitSaleAmount,
                FinalUnitPrice = finalUnitPrice,
                LineGross = lineGross,
                LineSale = lineSale,
                LineNet = lineNet
            };
        }).ToList();

        var grossSubtotal = Round2(lineCalcs.Sum(x => x.LineGross)); // before sales
        var totalSaleAmount = Round2(lineCalcs.Sum(x => x.LineSale)); // sum of all line sale amounts
        var netSubtotal = Round2(lineCalcs.Sum(x => x.LineNet));      // after sales (for reference)

        using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        // Recheck stock inside the transaction
        var currentStock = await _inventory.GetAvailableQuantitiesAsync(branchId, variantIds);
        if (itemGroups.Any(item =>
                currentStock.GetValueOrDefault(item.VariantId) < item.Quantity))
            throw new ArgumentException("Insufficient stock.");

        // === Discount & totals ===
        // EXTRA DISCOUNT MUST BE FROM THE FULL SUBTOTAL (not subtotal - sale)
        var extraDiscount = ComputeExtraDiscount(grossSubtotal, discountType.Code, dto.DiscountValue);

        // TAX on the full SUBTOTAL (pre discounts), per your requirement
        var taxAmount = Round2(grossSubtotal * taxRate);

        // Always set initial order status to Draft - payment will be handled separately
        var order = new Order
        {
            OrderNumber = await _documentNumbers.NextAsync(DocumentPrefixes.Order),
            OrderStatusId = await _lookups.IdAsync<OrderStatus>(OrderStatusCodes.Draft),
            OrderTypeId = await _lookups.IdAsync<OrderType>(
                deliveryCharge.IsInStore ? OrderTypeCodes.Pos : OrderTypeCodes.Delivery),
            CustomerId = customer.Id,
            DeliveryLocationId = deliveryCharge.Id,
            CreatedByEmployeeId = dto.CreatedByEmployeeId,
            CreatedAt = DateTime.UtcNow,
            BranchId = branchId
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var orderItems = lineCalcs.Select(line => new OrderItem
        {
            OrderId = order.Id,
            ProductVariantId = line.VariantId,
            Quantity = line.Quantity
        }).ToList();

        _db.OrderItems.AddRange(orderItems);
        await _db.SaveChangesAsync();

        // Deduct FEFO and remember exactly which lots supplied each order item.
        var saleMovementTypeId = await _lookups.IdAsync<MovementType>(MovementTypeCodes.Sale);
        foreach (var orderItem in orderItems)
        {
            var allocations = await _inventory.DeductAvailableQuantityAsync(
                branchId,
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
                    BranchId = branchId,
                    MovementTypeId = saleMovementTypeId,
                    Quantity = -allocation.Value,
                    CreatedAt = DateTime.UtcNow,
                    Reason = $"Order #{order.Id}"
                });
            }
        }

        var total = Round2(grossSubtotal + taxAmount + deliveryCharge.Fee - totalSaleAmount - extraDiscount);
        
        var invoice = new Invoice
        {
            InvoiceNumber = await _documentNumbers.NextAsync(DocumentPrefixes.Invoice),
            OrderId = order.Id,
            IssuedAt = DateTime.UtcNow,

            TaxRateId = taxRateEntity.Id,
            DeliveryChargeAmount = deliveryCharge.Fee,

            Subtotal = grossSubtotal,       // before any discounts
            SaleAmount = totalSaleAmount,   // item-level sales/promos

            DiscountTypeId = discountType.Id,
            DiscountValue = dto.DiscountValue,
            DiscountAmount = extraDiscount, // computed from FULL subtotal

            TaxAmount = taxAmount,
            TotalAmount = total,

            InvoiceStatusId = await _lookups.IdAsync<InvoiceStatus>(InvoiceStatusCodes.Issued)
        };

        _db.Invoices.Add(invoice);

        // Save first so invoice gets a real Id (needed for InvoiceItems FK)
        await _db.SaveChangesAsync();

        // Add invoice items
        foreach (var line in lineCalcs)
        {
            _db.InvoiceItems.Add(new InvoiceItem
            {
                InvoiceId = invoice.Id,
                ProductVariantId = line.VariantId,
                Quantity = line.Quantity,
                BaseUnitPrice = line.BaseUnitPrice,
                PerUnitSaleAmount = line.PerUnitSaleAmount,
                FinalUnitPrice = line.FinalUnitPrice,
                LineGross = line.LineGross,
                LineSale = line.LineSale,
                LineNet = line.LineNet
            });
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return new
        {
            orderId = order.Id,
            invoiceId = invoice.Id
        };
    }

    public async Task<object> UpdateOrderStatusAsync(
        int orderId,
        UpdateOrderStatusRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Action))
            throw new ArgumentException("Action is required (ship or cancel).");

        var action = request.Action.Trim().ToLower();

        if (action != "ship" && action != "cancel" && action != "deliver")
            throw new ArgumentException("Invalid action. Allowed: ship, deliver, cancel.");

        // Load items only when needed for cancellation
        var query = _db.Orders.Include(o => o.OrderStatus).AsQueryable();
        if (action == "cancel")
            query = query.Include(o => o.OrderItems);

        var order = await query.SingleOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
            throw new KeyNotFoundException($"Order {orderId} not found.");

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

        if (action == "deliver")
        {
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

        // cancel flow
        if (status == OrderStatusCodes.FullyPaid)
            throw new ArgumentException("Order is already paid, cannot cancel.");

        var returnMovementTypeId = await _lookups.IdAsync<MovementType>(MovementTypeCodes.ReturnFromClient);
        foreach (var item in order.OrderItems)
        {
            var returnedLots = await _inventory.ReturnSoldQuantityAsync(
                order.Id,
                item.ProductVariantId,
                order.BranchId,
                item.Quantity);

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
                    Reason = $"Order #{orderId} cancelled"
                });
            }
        }

        order.OrderStatusId = await _lookups.IdAsync<OrderStatus>(OrderStatusCodes.Cancelled);
        order.CancelledAt = DateTime.UtcNow;

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

    public async Task<object> GetOrderDetailsAsync(int orderId)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
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
                    .Include(p => p.Currency)
                    .AsEnumerable()
                    .Sum(p => p.Currency != null && p.Currency.ExchangeRate > 0
                        ? p.Amount / p.Currency.ExchangeRate
                        : p.Amount),
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
