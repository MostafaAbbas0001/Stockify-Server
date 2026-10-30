namespace Stockify.Controllers;

[ApiController]
[Route("api/expenses")]
public class ExpensesController : ControllerBase
{
    private readonly ExpenseService _expenseService;

    public ExpensesController(ExpenseService expenseService)
    {
        _expenseService = expenseService;
    }

    /// Creates a new expense with multiple lines using a single transaction.
    [HttpPost]
    [RequirePermission("expense.create")]
    public async Task<IActionResult> CreateExpense([FromBody] CreateExpenseRequest dto)
    {
        var userId = User.GetUserId();
        var result = await _expenseService.CreateExpenseAsync(dto, userId);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// Gets a single expense by ID with all lines and category names.
    [HttpGet("{id:int}")]
    [RequirePermission("expense.view")]
    public async Task<IActionResult> GetExpenseById(int id)
    {
        var result = await _expenseService.GetExpenseByIdAsync(id);
        return Ok(result);
    }

    [HttpGet]
    [RequirePermission("expense.view")]
    public async Task<IActionResult> ListExpenses(
        [FromQuery] string? search,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? categoryId,
        [FromQuery] int? branchId,
        [FromQuery] int? statusId,
        [FromQuery] int page = 1)
    {
        var result = await _expenseService.ListExpensesAsync(
            search,
            from,
            to,
            categoryId,
            branchId,
            statusId,
            page);
        return Ok(result);
    }

    [HttpGet("categories")]
    [RequirePermission("expense.view")]
    public async Task<IActionResult> ListExpenseCategories([FromQuery] int? parentId)
    {
        var result = await _expenseService.ListExpenseCategoriesAsync(parentId);
        return Ok(result);
    }

    [HttpPost("lines/payments")]
    [RequirePermission("expense.create")]
    public async Task<IActionResult> CreatePayment([FromBody] CreateExpenseLinePaymentRequest dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        var userId = User.GetUserId();
        var result = await _expenseService.CreatePaymentAsync(dto, userId);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("lines/payments/{id:int}/decision")]
    [RequirePermission("expenseDecision.submit")]
    public async Task<IActionResult> MakePaymentDecision(
        int id,
        [FromBody] ExpensePaymentDecisionRequest dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userId = User.GetUserId();
        var result = await _expenseService.MakePaymentDecisionAsync(id, dto, userId);
        return Ok(result);
    }

    [HttpDelete("lines/payments/{id:int}")]
    [RequirePermission("expense.create")]
    public async Task<IActionResult> DeletePayment(int id)
    {
        await _expenseService.DeletePaymentAsync(id);
        return NoContent();
    }
}

