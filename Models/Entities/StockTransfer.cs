namespace Stockify.Models.Entities;

public class StockTransfer : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    
    // Branch relationships
    public int? SourceBranchId { get; set; }
    public Branch? SourceBranch { get; set; }
    
    public int DestinationBranchId { get; set; }
    public Branch DestinationBranch { get; set; } = null!;
    
    // Transfer status tracking
    public int StatusId { get; set; }
    public StockTransferStatus Status { get; set; } = null!;
    
    // Metadata
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }      // When source shipped
    public DateTime? ReceivedAt { get; set; }  // When destination received
    
    public int CreatedByUserId { get; set; }
    public User CreatedBy { get; set; } = null!;
    
    // Discrepancy tracking
    public bool HasDiscrepancy { get; set; } = false;
    public bool IsResolved { get; set; } = true;
    public string? ResolutionNote { get; set; }
    
    // Navigation
    public ICollection<StockTransferItem> Items { get; set; } = new List<StockTransferItem>();
}
