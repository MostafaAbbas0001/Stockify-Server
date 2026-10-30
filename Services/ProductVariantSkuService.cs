using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Stockify.Services;

/// <summary>
/// Owns the generated SKU format and keeps stored variant SKUs synchronized
/// whenever a brand, product, attribute, or selected attribute value changes.
/// </summary>
public class ProductVariantSkuService
{
    private const int MaximumSkuLength = 90;
    private readonly AppDbContext _db;

    public ProductVariantSkuService(AppDbContext db)
    {
        _db = db;
    }

    public string BuildSku(
        string brandName,
        string productName,
        IEnumerable<ProductAttributeValue> values)
    {
        var selectedValues = values.ToList();
        return BuildSku(
            brandName,
            productName,
            selectedValues.Select(value => value.ProductAttribute),
            selectedValues);
    }

    private string BuildSku(
        string brandName,
        string productName,
        IEnumerable<ProductAttribute> assignedAttributes,
        IEnumerable<ProductAttributeValue> values)
    {
        var valueByAttribute = values.ToDictionary(
            value => value.ProductAttributeId,
            value => value.Value);
        var segments = new List<string> { brandName, productName };

        foreach (var attribute in assignedAttributes
                     .DistinctBy(attribute => attribute.Id)
                     .OrderBy(attribute => attribute.Name)
                     .ThenBy(attribute => attribute.Id))
        {
            segments.Add(attribute.Name);
            segments.Add(valueByAttribute.GetValueOrDefault(attribute.Id) ?? "UNASSIGNED");
        }

        return Normalize(string.Join("-", segments));
    }

    public Task RefreshVariantAsync(ProductVariant variant)
    {
        return ApplyAsync(new[] { variant });
    }

    public async Task EnsureAvailableAsync(string sku, int? excludingVariantId = null)
    {
        if (await _db.ProductVariants.AsNoTracking().AnyAsync(variant =>
                variant.Id != excludingVariantId && variant.SKU == sku))
            throw new InvalidOperationException(
                "The generated SKU conflicts with another product variant.");
    }

    public async Task RefreshForProductAsync(int productId)
    {
        await ApplyAsync(await VariantQuery()
            .Where(variant => variant.ProductId == productId)
            .ToListAsync());
    }

    public async Task RefreshForBrandAsync(int brandId)
    {
        await ApplyAsync(await VariantQuery()
            .Where(variant => variant.Product.BrandId == brandId)
            .ToListAsync());
    }

    public async Task RefreshForAttributeAsync(int attributeId)
    {
        await ApplyAsync(await VariantQuery()
            .Where(variant => variant.AttributeValues.Any(value =>
                value.ProductAttributeId == attributeId))
            .ToListAsync());
    }

    public async Task RefreshForAttributeValueAsync(int valueId)
    {
        await ApplyAsync(await VariantQuery()
            .Where(variant => variant.AttributeValues.Any(value => value.Id == valueId))
            .ToListAsync());
    }

    private IQueryable<ProductVariant> VariantQuery()
    {
        return _db.ProductVariants
            .Include(variant => variant.Product)
                .ThenInclude(product => product.Brand)
            .Include(variant => variant.Product)
                .ThenInclude(product => product.Attributes)
            .Include(variant => variant.AttributeValues)
                .ThenInclude(value => value.ProductAttribute);
    }

    private async Task ApplyAsync(IReadOnlyCollection<ProductVariant> variants)
    {
        if (variants.Count == 0)
            return;

        var generated = variants
            .Select(variant => new
            {
                Variant = variant,
                Sku = BuildSku(
                    variant.Product.Brand.Name,
                    variant.Product.Name,
                    variant.Product.Attributes,
                    variant.AttributeValues)
            })
            .ToList();

        if (generated
            .GroupBy(item => item.Sku, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
            throw new InvalidOperationException(
                "The selected product and attribute values would generate duplicate SKUs.");

        var affectedIds = generated
            .Where(item => item.Variant.Id > 0)
            .Select(item => item.Variant.Id)
            .ToArray();
        var targetSkus = generated.Select(item => item.Sku).ToArray();

        if (await _db.ProductVariants.AsNoTracking().AnyAsync(variant =>
                !affectedIds.Contains(variant.Id) && targetSkus.Contains(variant.SKU)))
            throw new InvalidOperationException(
                "The generated SKU conflicts with another product variant.");

        foreach (var item in generated)
            item.Variant.SKU = item.Sku;
    }

    private static string Normalize(string value)
    {
        var normalized = Regex.Replace(value.Trim(), "[^a-zA-Z0-9]+", "-")
            .Trim('-')
            .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("A SKU cannot be generated from empty product data.");

        if (normalized.Length <= MaximumSkuLength)
            return normalized;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..8];
        return $"{normalized[..(MaximumSkuLength - hash.Length - 1)].TrimEnd('-')}-{hash}";
    }
}
