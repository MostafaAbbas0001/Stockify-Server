namespace Stockify.Models.Entities;

/// <summary>Per-tenant counter for document numbers (ORD-1, INV-1, ...).</summary>
public class TenantSequence
{
    public int TenantId { get; set; }
    public string Name { get; set; } = null!;
    public long NextValue { get; set; } = 1;
}
