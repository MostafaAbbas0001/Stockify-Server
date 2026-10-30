namespace Stockify.Controllers;

[ApiController]
[Route("api/branches")]
public class BranchesController : ControllerBase
{
    private readonly BranchService _branchService;

    public BranchesController(BranchService branchService)
    {
        _branchService = branchService;
    }

    [HttpPost]
    [RequirePermission("branch.create")]
    public async Task<IActionResult> Create([FromBody] NameRequest dto)
    {
        var result = await _branchService.CreateAsync(new Branch { Name = dto.Name });
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:int}")]
    [RequirePermission("branch.update")]
    public async Task<IActionResult> Update(int id, [FromBody] NameRequest dto)
    {
        var result = await _branchService.UpdateAsync(id, new Branch { Name = dto.Name });
        return Ok(result);
    }

    [HttpGet]
    [RequirePermission("branch.view")]
    public async Task<IActionResult> GetAll()
    {
        var branches = await _branchService.GetAllAsync();
        return Ok(branches);
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("branch.delete")]
    public async Task<IActionResult> Delete(int id)
    {
        await _branchService.DeleteAsync(id);
        return NoContent();
    }
}


