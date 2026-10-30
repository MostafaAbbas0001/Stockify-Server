namespace Stockify.Models.Dtos;

public class CreateInvoiceReturnRequest
{
    public List<InvoiceReturnItemRequest> Items { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}
