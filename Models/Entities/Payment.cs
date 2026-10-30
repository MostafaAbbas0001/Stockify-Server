namespace Stockify.Models.Entities;

public class Payment : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    public decimal Amount { get; set; }
    public int PaymentMethodId { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = null!;
    public string? Reference { get; set; }
    
    public int CurrencyId { get; set; }
    public Currency Currency { get; set; } = null!;
    
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
}