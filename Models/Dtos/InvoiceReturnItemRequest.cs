namespace Stockify.Models.Dtos;

public class InvoiceReturnItemRequest
{
    public int InvoiceItemId { get; set; }
    public int Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}
