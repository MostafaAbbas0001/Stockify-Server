using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

public class CreateExpenseLineRequest
{
    [Required]
    public int CategoryId { get; set; }

    [Required]
    public decimal Amount { get; set; }

    public string? Note { get; set; }
    public int? EmployeeId { get; set; }
    public int? CurrencyId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? RentType { get; set; }
    public string? ItemName { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public int? Quantity { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? Rate { get; set; }
    /// <summary>Id of an ExpenseLineTypes row; decides how the amount is calculated.</summary>
    public int? LineTypeId { get; set; }
    public string? SupplierName { get; set; }
    public int? BranchId { get; set; }
    public string? AdditionalInfo { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime? ExpensePeriodStart { get; set; }
    public DateTime? ExpensePeriodEnd { get; set; }
}
