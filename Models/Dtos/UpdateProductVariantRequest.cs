using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

public class UpdateProductVariantRequest
{
    public decimal? Price { get; set; }

    /// <summary>false takes the variant off sale (it stays in history); true puts it back.</summary>
    public bool? IsSellable { get; set; }
    public decimal? NetPrice { get; set; }
    public int? SaleId { get; set; }
    public string? Barcode { get; set; }
    public List<ProductVariantAttributeSelectionRequest>? Attributes { get; set; }

    [MaxLength(2048)]
    public string? ImageUrl { get; set; }
}
