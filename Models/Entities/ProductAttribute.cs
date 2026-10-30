using System.Text.Json.Serialization;

namespace Stockify.Models.Entities;

public class ProductAttribute : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public ICollection<ProductAttributeValue> Values { get; set; } = new List<ProductAttributeValue>();

    [JsonIgnore]
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
