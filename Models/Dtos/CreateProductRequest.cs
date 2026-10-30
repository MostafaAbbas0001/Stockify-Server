using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

public class CreateProductRequest
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    [MaxLength(2048)]
    public string? ImageUrl { get; set; }

    public int BrandId { get; set; }
    public int CategoryId { get; set; }
    public List<int> AttributeIds { get; set; } = new();
}
