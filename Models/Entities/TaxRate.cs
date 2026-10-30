namespace Stockify.Models.Entities;

public class TaxRate : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public decimal Rate { get; set; }
    // The rate applied to new orders; exactly one per tenant.
    public bool IsDefault { get; set; }

    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}
