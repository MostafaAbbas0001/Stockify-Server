using System.Text.Json.Serialization;

namespace Stockify.Models.Entities;

public class ProductAttributeValue : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int ProductAttributeId { get; set; }

    [JsonIgnore]
    public ProductAttribute ProductAttribute { get; set; } = null!;

    public string Value { get; set; } = null!;

    [JsonIgnore]
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
}
