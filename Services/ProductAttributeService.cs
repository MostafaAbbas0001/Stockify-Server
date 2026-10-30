namespace Stockify.Services;

public class ProductAttributeService
{
    private readonly AppDbContext _db;
    private readonly ProductVariantSkuService _skuService;

    public ProductAttributeService(AppDbContext db, ProductVariantSkuService skuService)
    {
        _db = db;
        _skuService = skuService;
    }

    public async Task<ProductAttribute> CreateAsync(CreateProductAttributeRequest dto)
    {
        var name = Normalize(dto.Name, "Attribute name");

        if (await _db.ProductAttributes.AnyAsync(a => a.Name.ToUpper() == name.ToUpper()))
            throw new InvalidOperationException("An attribute with the same name already exists.");

        var values = dto.Values
            .Select(value => Normalize(value, "Attribute value"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new ProductAttributeValue { Value = value })
            .ToList();

        var attribute = new ProductAttribute
        {
            Name = name,
            Values = values
        };

        _db.ProductAttributes.Add(attribute);
        await _db.SaveChangesAsync();
        return attribute;
    }

    public async Task<IEnumerable<object>> GetAllAsync()
    {
        return await _db.ProductAttributes
            .AsNoTracking()
            .OrderBy(attribute => attribute.Name)
            .Select(attribute => new
            {
                attribute.Id,
                attribute.Name,
                Values = attribute.Values
                    .OrderBy(value => value.Value)
                    .Select(value => new { value.Id, value.Value })
                    .ToList()
            })
            .ToListAsync();
    }

    public async Task<object> GetByIdAsync(int id)
    {
        var attribute = await _db.ProductAttributes
            .AsNoTracking()
            .Where(attribute => attribute.Id == id)
            .Select(attribute => new
            {
                attribute.Id,
                attribute.Name,
                Values = attribute.Values
                    .OrderBy(value => value.Value)
                    .Select(value => new { value.Id, value.Value })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        return attribute ?? throw new KeyNotFoundException("Attribute not found.");
    }

    public async Task<ProductAttribute> UpdateAsync(
        int id,
        UpdateProductAttributeRequest dto)
    {
        var attribute = await _db.ProductAttributes.FindAsync(id)
            ?? throw new KeyNotFoundException("Attribute not found.");

        var name = Normalize(dto.Name, "Attribute name");
        if (await _db.ProductAttributes.AnyAsync(a => a.Id != id && a.Name.ToUpper() == name.ToUpper()))
            throw new InvalidOperationException("An attribute with the same name already exists.");

        attribute.Name = name;
        await _skuService.RefreshForAttributeAsync(id);
        await _db.SaveChangesAsync();
        return attribute;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var attribute = await _db.ProductAttributes.FindAsync(id)
            ?? throw new KeyNotFoundException("Attribute not found.");

        if (await _db.Products.AnyAsync(product => product.Attributes.Any(attribute => attribute.Id == id)))
            throw new InvalidOperationException("Cannot delete an attribute that is assigned to products.");

        if (await _db.ProductVariants.AnyAsync(variant =>
                variant.AttributeValues.Any(value => value.ProductAttributeId == id)))
            throw new InvalidOperationException("Cannot delete an attribute that is used by product variants.");

        _db.ProductAttributes.Remove(attribute);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<ProductAttributeValue> CreateValueAsync(
        int attributeId,
        CreateProductAttributeValueRequest dto)
    {
        if (!await _db.ProductAttributes.AnyAsync(attribute => attribute.Id == attributeId))
            throw new KeyNotFoundException("Attribute not found.");

        var value = Normalize(dto.Value, "Attribute value");
        if (await _db.ProductAttributeValues.AnyAsync(item =>
                item.ProductAttributeId == attributeId && item.Value.ToUpper() == value.ToUpper()))
            throw new InvalidOperationException("This value already exists for the attribute.");

        var item = new ProductAttributeValue
        {
            ProductAttributeId = attributeId,
            Value = value
        };

        _db.ProductAttributeValues.Add(item);
        await _db.SaveChangesAsync();
        return item;
    }

    public async Task<ProductAttributeValue> UpdateValueAsync(
        int attributeId,
        int valueId,
        UpdateProductAttributeValueRequest dto)
    {
        var item = await _db.ProductAttributeValues.FirstOrDefaultAsync(value =>
            value.Id == valueId && value.ProductAttributeId == attributeId)
            ?? throw new KeyNotFoundException("Attribute value not found.");

        var value = Normalize(dto.Value, "Attribute value");
        if (await _db.ProductAttributeValues.AnyAsync(other =>
                other.Id != valueId &&
                other.ProductAttributeId == attributeId &&
                other.Value.ToUpper() == value.ToUpper()))
            throw new InvalidOperationException("This value already exists for the attribute.");

        item.Value = value;
        await _skuService.RefreshForAttributeValueAsync(valueId);
        await _db.SaveChangesAsync();
        return item;
    }

    public async Task<bool> DeleteValueAsync(int attributeId, int valueId)
    {
        var item = await _db.ProductAttributeValues.FirstOrDefaultAsync(value =>
            value.Id == valueId && value.ProductAttributeId == attributeId)
            ?? throw new KeyNotFoundException("Attribute value not found.");

        if (await _db.ProductVariants.AnyAsync(variant =>
                variant.AttributeValues.Any(value => value.Id == valueId)))
            throw new InvalidOperationException("Cannot delete an attribute value that is used by product variants.");

        _db.ProductAttributeValues.Remove(item);
        await _db.SaveChangesAsync();
        return true;
    }

    private static string Normalize(string? value, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException($"{fieldName} is required.");

        return normalized;
    }
}
