namespace Stockify.Models.Dtos;

public class UpdateSaleRequest
{
    public string Name { get; set; } = null!;
    /// <summary>Id of a SaleTypes row.</summary>
    public int SaleTypeId { get; set; }
    public decimal Value { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
