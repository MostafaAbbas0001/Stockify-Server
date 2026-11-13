namespace Stockify.Models.Entities;

/// <summary>
/// Remembers which sale a POS checkout key produced, so a retried or double-clicked checkout returns
/// the first sale instead of creating a second one.
/// </summary>
public class CheckoutKey : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string Key { get; set; } = null!;

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    /// <summary>Change given at the original checkout, in the payment currency.</summary>
    public decimal Change { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
