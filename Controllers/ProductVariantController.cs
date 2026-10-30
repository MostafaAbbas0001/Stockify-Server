namespace Stockify.Controllers;

[ApiController]
[Route("api/product-variants")]
public class ProductVariantController : ControllerBase
{
    private readonly ProductVariantService _service;

    public ProductVariantController(ProductVariantService service)
    {
        _service = service;
    }

    [HttpPost]
    [RequirePermission("variant.create")]
    public async Task<IActionResult> Create([FromBody] CreateProductVariantRequest dto)
    {
        var result = await _service.CreateAsync(dto);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet]
    [RequirePermission("variant.view")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] int? productId,
        [FromQuery] int? brandId,
        [FromQuery] int? categoryId,
        [FromQuery] bool? isSellable,
        [FromQuery] int page = 1)
    {
        var rows = await _service.GetAllAsync(
            search,
            productId,
            brandId,
            categoryId,
            isSellable,
            page);
        return Ok(rows);
    }

    [HttpGet("{id:int}/location-stock")]
    [RequirePermission("variant.view")]
    public async Task<IActionResult> GetVariantLocationStock(int id)
    {
        var result = await _service.GetVariantLocationStockAsync(id);
        return Ok(result);
    }

    [HttpPatch("{id:int}")]
    [RequirePermission("variant.update")]
    public async Task<IActionResult> UpdateVariant(
        int id,
        [FromBody] UpdateProductVariantRequest dto)
    {
        var result = await _service.UpdateAsync(id, dto);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("variant.delete")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }
}



