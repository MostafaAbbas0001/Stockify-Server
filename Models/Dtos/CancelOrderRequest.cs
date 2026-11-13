using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

public class CancelOrderRequest
{
    [MaxLength(500)]
    public string? Reason { get; set; }
}
