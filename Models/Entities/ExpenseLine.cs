namespace Stockify.Models.Entities;

public class ExpenseLine : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    
    public int ExpenseId { get; set; }
    public Expense Expense { get; set; } = null!;

    public int CategoryId { get; set; }
    public ExpenseCategory Category { get; set; } = null!;
    
    public decimal TotalAmount { get; set; }
    
    public int? CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    
    // Payment status
    public int StatusId { get; set; }
    public ExpenseStatus Status { get; set; } = null!;
    
    public ICollection<ExpenseLinePayment> Payments { get; set; } = new List<ExpenseLinePayment>();

    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public string? ItemName { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public int? Quantity { get; set; }

    public decimal? Rate { get; set; }

    public decimal? UnitCost { get; set; }
    public string? SupplierName { get; set; }

    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public int? LineTypeId { get; set; }
    public ExpenseLineType? LineType { get; set; }
    public string? Note { get; set; }
    public string? AdditionalInfo { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime? ExpensePeriodStart { get; set; }
    public DateTime? ExpensePeriodEnd { get; set; } 
}
