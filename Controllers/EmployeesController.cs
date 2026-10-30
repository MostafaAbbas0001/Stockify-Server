namespace Stockify.Controllers;

[ApiController]
[Route("api/employees")]
public class EmployeesController : ControllerBase
{
    private readonly EmployeeService _employeeService;

    public EmployeesController(EmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    [HttpPost]
    [RequirePermission("employee.create")]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeRequest dto)
    {
        var result = await _employeeService.CreateAsync(dto);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("{id:int}")]
    [RequirePermission("employee.view")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _employeeService.GetByIdAsync(id);
        return Ok(result);
    }

    [HttpGet]
    [RequirePermission("employee.view")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] int? branchId,
        [FromQuery] bool? isSale,
        [FromQuery] int page = 1)
    {
        // Without branch.all.access a user only sees employees of their own branch.
        var branchScope = User.HasPermission("branch.all.access") ? branchId : User.GetBranchId();

        var result = await _employeeService.GetAllAsync(
            search,
            branchScope,
            isSale,
            page);

        return Ok(result);
    }

    [HttpGet("current-branch")]
    public async Task<IActionResult> GetMyBranchEmployees()
    {
        var branchId = User.GetBranchId();
        var result = await _employeeService.GetMyBranchEmployeesAsync(branchId);

        return Ok(result);
    }

    [HttpPut("{id:int}")]
    [RequirePermission("employee.update")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateEmployeeRequest dto)
    {
        var result = await _employeeService.UpdateAsync(id, dto);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("employee.delete")]
    public async Task<IActionResult> Delete(int id)
    {
        await _employeeService.DeleteAsync(id);
        return NoContent();
    }

    [HttpGet("{employeeId:int}/sales-summary")]
    [RequirePermission("employee.view")]
    public async Task<IActionResult> GetOrderAmountsPerEmployee(
        int employeeId,
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate)
    {
        if (startDate > endDate)
            return BadRequest(new
            {
                code = "BAD_REQUEST",
                message = "Start date cannot be greater than end date."
            });

        var request = new EmployeeSalesRequest
        {
            EmployeeId = employeeId,
            StartDate = startDate,
            EndDate = endDate
        };

        var totalSales = await _employeeService.GetOrderAmountsPerEmployee(request);

        return Ok(new
        {
            employeeId,
            startDate,
            endDate,
            totalSales
        });
    }
}
