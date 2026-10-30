namespace Stockify.Models.Entities;

public class ExpenseCategory : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Code { get; set; }
    
    // Self-referencing hierarchy
    public int? ParentId { get; set; }
    public ExpenseCategory? Parent { get; set; }
    public ICollection<ExpenseCategory> Children { get; set; } = new List<ExpenseCategory>();
    
}
