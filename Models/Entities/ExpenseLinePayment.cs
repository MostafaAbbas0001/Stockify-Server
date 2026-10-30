namespace Stockify.Models.Entities;

public class ExpenseLinePayment : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int ExpenseLineId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public int PaymentMethodId { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = null!;
    public string? PaymentReference { get; set; }
    public string? Notes { get; set; }
    
    // Audit fields
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int? ApprovedByUserId { get; set; }
    public string? RejectionReason { get; set; }
    
    // Navigation properties
    public ExpenseLine ExpenseLine { get; set; } = null!;
    public User? CreatedBy { get; set; }
    public User? ApprovedBy { get; set; }
}
