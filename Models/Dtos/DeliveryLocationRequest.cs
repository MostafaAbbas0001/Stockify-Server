using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

/// <summary>Create/update body for a delivery location and its fee.</summary>
public class DeliveryLocationRequest
{
    [Required]
    [MaxLength(200)]
    public string LocationName { get; set; } = null!;

    [Range(0, double.MaxValue)]
    public decimal Fee { get; set; }
}
