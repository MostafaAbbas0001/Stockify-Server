using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

public class CreateProductVariantRequest
{
    public int ProductId { get; set; }
    public List<ProductVariantAttributeSelectionRequest> Attributes { get; set; } = new();
    public decimal Price { get; set; }
    public string? Barcode { get; set; }
    public decimal NetPrice { get; set; }
    /// <summary>Optional sale (promotion); null or 0 means none.</summary>
    public int? SaleId { get; set; }

    [MaxLength(2048)]
    public string? ImageUrl { get; set; }
}
