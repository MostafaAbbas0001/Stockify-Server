using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

public class ResolveStockTransferDiscrepancyRequest
{
    [Required]
    [MinLength(3)]
    public string Note { get; set; } = string.Empty;
}
