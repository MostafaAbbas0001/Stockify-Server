using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

/// <summary>Create/update body for branches, brands and categories.</summary>
public class NameRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = null!;
}
