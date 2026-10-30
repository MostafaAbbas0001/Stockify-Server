namespace Stockify.Models.Entities;

public class ProductVariant : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    // Null means the variant is not part of any sale.
    public int? SaleId { get; set; }
    public Sale? Sale { get; set; }
    public string SKU { get; set; } = null!;
    public decimal Price { get; set; }
    public decimal NetPrice { get; set; }
    public string? ImageUrl { get; set; }

    // POS: Barcode per variant (nullable initially for migration friendliness)
    public string? Barcode { get; set; }
    // POS: Whether this variant is allowed to be sold at POS
    public bool IsSellable { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<ProductAttributeValue> AttributeValues { get; set; } = new List<ProductAttributeValue>();
    public ICollection<InventoryLot> InventoryLots { get; set; } = new List<InventoryLot>();
    public ICollection<LocationStock> LocationStocks { get; set; } = new List<LocationStock>();
    public ICollection<CreditMemoItem> CreditMemoItems { get; set; } = new List<CreditMemoItem>();
}

