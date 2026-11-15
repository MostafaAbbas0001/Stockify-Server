namespace Stockify.Controllers;

[ApiController]
[Route("api/sales")]
public class SalesController : ControllerBase
{
    private readonly SaleService _saleService;
    public SalesController(SaleService saleService) => _saleService = saleService;

    [HttpGet]
    [RequirePermission("sale.view")]
    public async Task<IActionResult> GetAllSales(
        [FromQuery] string? search,
        [FromQuery] int? saleTypeId,
        [FromQuery] bool? isActive,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int page = 1)
    {
        var data = await _saleService.GetAllSalesAsync(
            search,
            saleTypeId,
            isActive,
            startDate,
            endDate,
            page);
        return Ok(data);
    }

    [HttpGet("{saleId:int}")]
    [RequirePermission("sale.view")]
    public async Task<IActionResult> GetSaleDetails(
        int saleId,
        [FromQuery] string? search,
        [FromQuery] int page = 1)
    {
        var result = await _saleService.GetSaleDetailsAsync(saleId, search, page);
        return Ok(result);
    }

    [HttpGet("variants")]
    [RequirePermission("sale.view")]
    public async Task<IActionResult> SearchVariants([FromQuery] string q)
    {
        var results = await _saleService.SearchVariantsAsync(q);
        return Ok(results);
    }

    [HttpPost]
    [RequirePermission("sale.create")]
    public async Task<IActionResult> CreateSale([FromBody] CreateSaleRequest sale)
    {
        var result = await _saleService.CreateSaleAsync(sale);
        return CreatedAtAction(nameof(GetSaleDetails), new { saleId = result!.Id }, result);
    }


    [HttpPut("{id:int}")]
    [RequirePermission("sale.update")]
    public async Task<IActionResult> UpdateSale(int id, [FromBody] UpdateSaleRequest sale)
    {
        var result = await _saleService.UpdateSaleAsync(id, sale);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("sale.delete")]
    public async Task<IActionResult> DeleteSale(int id)
    {
        await _saleService.DeleteSaleAsync(id);
        return NoContent();
    }

    [HttpPost("{saleId:int}/variants")]
    [RequirePermission("sale.assignVariant")]
    public async Task<IActionResult> AssignVariants(int saleId, [FromBody] AssignSaleVariantsRequest request)
    {
        var assigned = await _saleService.AssignVariantsAsync(saleId, request.VariantIds);
        return Ok(new { assigned });
    }

    [HttpDelete("{saleId:int}/variants/{variantId:int}")]
    [RequirePermission("sale.assignVariant")]
    public async Task<IActionResult> RemoveVariant(int saleId, int variantId)
    {
        await _saleService.RemoveVariantAsync(saleId, variantId);
        return NoContent();
    }
}
