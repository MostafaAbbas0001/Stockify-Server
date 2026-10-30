namespace Stockify.Models.Dtos;

public class CreateInventoryLotRequest
{
    public required int VariantId { get; set; }
    public required string LotNumber { get; set; }
    public DateOnly? ManufacturingDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
}
