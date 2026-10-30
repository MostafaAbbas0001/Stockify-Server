namespace Stockify.Controllers;

[ApiController]
[Route("api/brands")]
public class BrandsController : ControllerBase
{
    private readonly BrandService _brandService;

    public BrandsController(BrandService brandService)
    {
        _brandService = brandService;
    }

    [HttpPost]
    [RequirePermission("brand.create")]
    public async Task<IActionResult> Create([FromBody] NameRequest dto)
    {
        var result = await _brandService.CreateAsync(new Brand { Name = dto.Name });
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:int}")]
    [RequirePermission("brand.update")]
    public async Task<IActionResult> Update(int id, [FromBody] NameRequest dto)
    {
        var result = await _brandService.UpdateAsync(id, new Brand { Name = dto.Name });
        return Ok(result);
    }

    [HttpGet]
    [RequirePermission("brand.view")]
    public async Task<IActionResult> GetAll()
    {
        var brands = await _brandService.GetAllAsync();
        return Ok(brands);
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("brand.delete")]
    public async Task<IActionResult> Delete(int id)
    {
        await _brandService.DeleteAsync(id);
        return NoContent();
    }
}
