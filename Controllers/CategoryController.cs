namespace Stockify.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoryController : ControllerBase
{
    private readonly CategoryService _categoryService;

    public CategoryController(CategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    [HttpGet]
    [RequirePermission("category.view")]
    public async Task<IActionResult> GetCategories()
    {
        var cats = await _categoryService.GetAllAsync();
        return Ok(cats);
    }

    [HttpPost]
    [RequirePermission("category.create")]
    public async Task<IActionResult> Create([FromBody] NameRequest dto)
    {
        var result = await _categoryService.CreateAsync(new Category { Name = dto.Name });
        return StatusCode(
            StatusCodes.Status201Created,
            new { result!.Id, result.Name });
    }

    [HttpPut("{id:int}")]
    [RequirePermission("category.update")]
    public async Task<IActionResult> Update(int id, [FromBody] NameRequest dto)
    {
        var result = await _categoryService.UpdateAsync(id, new Category { Name = dto.Name });
        return Ok(new { result!.Id, result.Name });
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("category.delete")]
    public async Task<IActionResult> Delete(int id)
    {
        await _categoryService.DeleteAsync(id);
        return NoContent();
    }

}

