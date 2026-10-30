namespace Stockify.Models.Entities;

public class Invoice : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = null!;

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int TaxRateId { get; set; }
    public TaxRate TaxRate { get; set; } = null!;

    public decimal Subtotal { get; set; }              
    public decimal TaxAmount { get; set; }
    public decimal DeliveryChargeAmount { get; set; }
    public decimal SaleAmount { get; set; }            
    public decimal DiscountAmount { get; set; }

    public int DiscountTypeId { get; set; }
    public DiscountType DiscountType { get; set; } = null!;
    public decimal DiscountValue { get; set; }

    public decimal TotalAmount { get; set; }
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    public int InvoiceStatusId { get; set; }
    public InvoiceStatus InvoiceStatus { get; set; } = null!;

    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<CreditMemo> CreditMemos { get; set; } = new List<CreditMemo>();
    public ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();
}
