namespace Stockify.Models.Entities;

public class Expense : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string? ExpenseNumber { get; set; }
    
    // Category
    public int ExpenseCategoryId { get; set; }
    public ExpenseCategory ExpenseCategory { get; set; } = null!;
        
    // Audit
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public int? CreatedByUserId { get; set; }
    public User? CreatedBy { get; set; }
    
}
