namespace Stockify.Models.Entities;

public class CreditMemo : ITenantOwned{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string CreditMemoNumber { get; set; } = null!;
    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public int? CustomerId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? IssuedAt { get; set; }
    public decimal TotalAmount { get; set; }
    public ICollection<CreditMemoItem> CreditMemoItems { get; set; } = new List<CreditMemoItem>();
}