namespace Stockify.Services;

public class PosService
{
    private readonly AppDbContext _db;
    private readonly InventoryLotService _inventory;

    public PosService(AppDbContext db, InventoryLotService inventory)
    {
        _db = db;
        _inventory = inventory;
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

        var taxRate = await _db.TaxRates
            .Where(t => t.IsDefault)
            .Select(t => t.Rate)
            .FirstOrDefaultAsync();

        var itemTax = variant.Price * taxRate;

        var now = DateTime.UtcNow;
        var sale = variant.Sale;
        var saleActive = sale != null &&
            (!sale.StartDate.HasValue || sale.StartDate <= now) &&
            (!sale.EndDate.HasValue || sale.EndDate >= now);

        decimal saleDiscount = 0;
        if (saleActive && sale != null)
        {
            saleDiscount = sale.SaleType.Code == SaleTypeCodes.Percentage
                ? variant.Price * (sale.Value / 100m)
                : sale.Value;
        }

        var subtotal = variant.Price - saleDiscount;
        var totalAmount = subtotal + itemTax;

        var result = new
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
            Subtotal = subtotal,
            SaleDiscount = saleDiscount,
            StockQuantity = stock,
            Barcode = variant.Barcode,
            IsSellable = variant.IsSellable,
            ItemTax = itemTax,
            TotalAmount = totalAmount,
            SaleTimeStart = sale?.StartDate,
            SaleEndTime = sale?.EndDate,
            SaleTypeId = sale?.SaleTypeId,
            SaleTypeCode = sale?.SaleType.Code,
            SaleValue = sale?.Value ?? 0
        };

        return result;
    }
}
