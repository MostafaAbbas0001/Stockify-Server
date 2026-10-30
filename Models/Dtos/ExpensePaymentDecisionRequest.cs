namespace Stockify.Models.Dtos;

public class ExpensePaymentDecisionRequest
{
    public bool IsApproved { get; set; }
    public string? RejectionReason { get; set; }
}
