namespace Stockify.Controllers;

[ApiController]
[Route("api/products")]

public class ProductController : ControllerBase
{
    private readonly ProductService _productService;

    public ProductController(ProductService productService)
    {
        _productService = productService;
    }

    [HttpPost]
    [RequirePermission("product.create")]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest productDto)
    {
        var result = await _productService.CreateAsync(productDto);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:int}")]
    [RequirePermission("product.update")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateProductRequest dto)
    {
        var result = await _productService.UpdateAsync(id, dto);
        return Ok(result);
    }

    [HttpGet]
    [RequirePermission("product.view")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] int? brandId,
        [FromQuery] int? categoryId,
        [FromQuery] int page = 1)
    {
        var products = await _productService.GetAllAsync(
            search,
            brandId,
            categoryId,
            page);
        return Ok(products);
    }

    [HttpGet("{id:int}")]
    [RequirePermission("product.view")]
    public async Task<IActionResult> GetById(int id)
    {
        var product = await _productService.GetByIdAsync(id);
        return Ok(product);
    }

    [HttpGet("{productId:int}/variants")]
    [RequirePermission("product.view")]
    public async Task<IActionResult> GetByProduct(
        int productId,
        [FromQuery] string? search,
        [FromQuery] int page = 1)
    {
        var rows = await _productService.GetByProductAsync(productId, search, page);
        return Ok(rows);
    }

    [HttpPut("{productId:int}/attributes")]
    [RequirePermission("product.update")]
    public async Task<IActionResult> SetAttributes(
        int productId,
        [FromBody] List<int> attributeIds)
    {
        var product = await _productService.SetAttributesAsync(productId, attributeIds);
        return Ok(product);
    }

    [HttpGet("catalog")]
    [RequirePermission("product.view")]
    public async Task<IActionResult> GetProductsWithVariants(
        [FromQuery] string? search,
        [FromQuery] int? brandId,
        [FromQuery] int? categoryId,
        [FromQuery] int page = 1)
    {
        var result = await _productService.GetProductsAndRelatedVariantsAsync(
            search,
            brandId,
            categoryId,
            page);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("product.delete")]
    public async Task<IActionResult> Delete(int id)
    {
        await _productService.DeleteAsync(id);
        return NoContent();
    }

}

