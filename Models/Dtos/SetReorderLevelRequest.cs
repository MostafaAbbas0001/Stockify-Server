using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

public class SetReorderLevelRequest
{
    [Range(1, int.MaxValue)]
    public int VariantId { get; set; }

    [Range(1, int.MaxValue)]
    public int BranchId { get; set; }

    [Range(0, int.MaxValue)]
    public int ReorderLevel { get; set; }
}
