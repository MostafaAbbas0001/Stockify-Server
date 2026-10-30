namespace Stockify.Models.Dtos;

public class CreateProductAttributeRequest
{
    public string Name { get; set; } = null!;
    public List<string> Values { get; set; } = new();
}
