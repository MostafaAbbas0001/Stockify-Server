namespace Stockify.Services;

public class PosService
{
    private readonly AppDbContext _db;
    private readonly InventoryLotService _inventory;
    private readonly LookupService _lookups;

    public PosService(AppDbContext db, InventoryLotService inventory, LookupService lookups)
    {
        _db = db;
        _inventory = inventory;
        _lookups = lookups;
    }

    public async Task<object> ScanAsync(string code, int branchId)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Barcode or SKU required");

        var normalized = code.Trim();

        var variant = await _db.ProductVariants
            .Include(v => v.Product)
                .ThenInclude(p => p.Brand)
            .Include(v => v.AttributeValues)
                .ThenInclude(value => value.ProductAttribute)
            .Include(v => v.Sale)
                .ThenInclude(sale => sale!.SaleType)
            .FirstOrDefaultAsync(v => v.Barcode == normalized || EF.Functions.Like(v.SKU, $"%{normalized}%"));

        if (variant == null)
            throw new KeyNotFoundException("Item not found");

        if (!variant.IsSellable)
            throw new ArgumentException("Item not sellable");

        var stock = await _inventory.GetAvailableQuantityAsync(branchId, variant.Id);
        var taxRate = await DefaultTaxRateAsync();

        // Same numbers the invoice will carry (Pricing is the single source of truth).
        var now = DateTime.UtcNow;
        var line = new Pricing.Line(variant.Id, 1, variant.Price, Pricing.PerUnitSaleAmount(variant.Price, variant.Sale, now));
        var itemTax = Pricing.Round2(line.FinalUnitPrice * taxRate);
        var sale = Pricing.SaleIsActive(variant.Sale, now) ? variant.Sale : null;

        return new
        {
            BrandName = variant.Product.Brand.Name,
            ProductName = variant.Product.Name,
            ImageUrl = variant.ImageUrl ?? variant.Product.ImageUrl,
            Attributes = variant.AttributeValues
                .OrderBy(item => item.ProductAttribute.Name)
                .Select(item => new
                {
                    AttributeId = item.ProductAttributeId,
                    Name = item.ProductAttribute.Name,
                    ValueId = item.Id,
                    item.Value
                }),
            SaleName = sale?.Name ?? string.Empty,
            SKU = variant.SKU,
            ProductId = variant.ProductId,
            VariantId = variant.Id,
            Price = variant.Price,
            Subtotal = line.FinalUnitPrice,
            SaleDiscount = line.PerUnitSaleAmount,
            StockQuantity = stock,
            Barcode = variant.Barcode,
            IsSellable = variant.IsSellable,
            ItemTax = itemTax,
            TotalAmount = Pricing.Round2(line.FinalUnitPrice + itemTax),
            SaleTimeStart = sale?.StartDate,
            SaleEndTime = sale?.EndDate,
            SaleTypeId = sale?.SaleTypeId,
            SaleTypeCode = sale?.SaleType.Code,
            SaleValue = sale?.Value ?? 0
        };
    }

    /// <summary>Prices a cart exactly as PlaceOrderAsync will, without touching stock.</summary>
    public async Task<object> PreviewAsync(CartPreviewRequest dto)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            throw new ArgumentException("Cart must have at least one item.");
        if (dto.Items.Any(item => item.Quantity <= 0))
            throw new ArgumentException("Each item quantity must be > 0.");

        var discountType = dto.DiscountTypeId is int discountTypeId
            ? await _lookups.FindAsync<DiscountType>(discountTypeId)
                ?? throw new ArgumentException($"Discount type {discountTypeId} does not exist.")
            : (await _lookups.ListAsync<DiscountType>()).First(type => type.Code == DiscountTypeCodes.None);

        var deliveryFee = 0m;
        if (dto.DeliveryLocationId is int locationId)
        {
            deliveryFee = await _db.DeliveryLocations
                .Where(location => location.Id == locationId)
                .Select(location => (decimal?)location.Fee)
                .FirstOrDefaultAsync()
                ?? throw new ArgumentException($"Delivery location {locationId} not found.");
        }

        var groups = dto.Items
            .GroupBy(item => item.ProductVariantId)
            .Select(group => new { VariantId = group.Key, Quantity = group.Sum(item => item.Quantity) })
            .ToList();
        var variantIds = groups.Select(group => group.VariantId).ToList();
        var variants = await _db.ProductVariants
            .AsNoTracking()
            .Include(v => v.Sale)
                .ThenInclude(sale => sale!.SaleType)
            .Where(v => variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id);
        if (variants.Count != variantIds.Count)
            throw new ArgumentException("One or more variants not found.");

        var now = DateTime.UtcNow;
        var lines = groups
            .Select(group =>
            {
                var variant = variants[group.VariantId];
                return new Pricing.Line(group.VariantId, group.Quantity, variant.Price, Pricing.PerUnitSaleAmount(variant.Price, variant.Sale, now));
            })
            .ToList();
        var totals = Pricing.OrderTotals(lines, discountType.Code, dto.DiscountValue, await DefaultTaxRateAsync(), deliveryFee);

        return new
        {
            totals.GrossSubtotal,
            totals.SaleAmount,
            totals.NetSubtotal,
            totals.DiscountAmount,
            totals.TaxAmount,
            totals.DeliveryFee,
            totals.Total,
            Lines = lines.Select(line => new
            {
                line.VariantId,
                line.Quantity,
                line.BaseUnitPrice,
                line.PerUnitSaleAmount,
                line.FinalUnitPrice,
                line.LineNet
            })
        };
    }

    private async Task<decimal> DefaultTaxRateAsync() =>
        await _db.TaxRates.Where(t => t.IsDefault).Select(t => t.Rate).FirstOrDefaultAsync();
}
