namespace Stockify.Models.Dtos;

public class CreateExpenseRequest
{
    public DateTime ExpenseDate { get; set; }
    public int ExpenseCategoryId { get; set; }
    public List<CreateExpenseLineRequest> Lines { get; set; } = new();
}
