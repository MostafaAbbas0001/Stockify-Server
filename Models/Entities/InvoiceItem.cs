namespace Stockify.Models.Entities;

public class InvoiceItem : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    
    public int ProductVariantId { get; set; }
    public int Quantity { get; set; }
    public decimal BaseUnitPrice { get; set; }
    public decimal PerUnitSaleAmount { get; set; }
    public decimal FinalUnitPrice { get; set; }
    public decimal LineGross { get; set; }
    public decimal LineSale { get; set; }
    public decimal LineNet { get; set; }


    // Navigation property for related CreditMemoItems
    public ICollection<CreditMemoItem> CreditMemoItems { get; set; } = new List<CreditMemoItem>();
}
