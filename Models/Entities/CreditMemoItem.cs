namespace Stockify.Models.Entities;

public class CreditMemoItem : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int CreditMemoId { get; set; }
    public CreditMemo CreditMemo { get; set; } = null!;
    public int InvoiceItemId { get; set; }
    public InvoiceItem InvoiceItem { get; set; } = null!;
    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;
    public int Quantity { get; set; }

    public decimal LineGross { get; set; }
    public decimal LineSale { get; set; }
    public decimal LineNet { get; set; }

    public decimal RefundAmount { get; set; }
    public string Reason { get; set; } = null!;
}