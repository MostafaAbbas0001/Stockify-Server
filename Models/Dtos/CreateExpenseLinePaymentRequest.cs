using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

public class CreateExpenseLinePaymentRequest
{
    [Required]
    public int ExpenseLineId { get; set; }

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public decimal Amount { get; set; }

    [Required]
    public DateTime PaymentDate { get; set; }

    [Required]
    [StringLength(50)]
    /// <summary>Id of a PaymentMethods row.</summary>
    public int PaymentMethodId { get; set; }

    [StringLength(100)]
    public string? PaymentReference { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public int? CreatedByUserId { get; set; }
}
