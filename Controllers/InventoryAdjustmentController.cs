namespace Stockify.Controllers;

[ApiController]
[Route("api/inventory-adjustments")]
public class InventoryAdjustmentController : ControllerBase
{
    private readonly InventoryAdjustmentService _adjustments;

    public InventoryAdjustmentController(
        InventoryAdjustmentService adjustments)
    {
        _adjustments = adjustments;
    }

    [HttpGet]
    [RequirePermission("stock.view")]
    public async Task<IActionResult> GetAdjustments(
        [FromQuery] string? search,
        [FromQuery] int? branchId,
        [FromQuery] int? variantId,
        [FromQuery] int? inventoryLotId,
        [FromQuery] int? minimumQuantity,
        [FromQuery] int? maximumQuantity,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int page = 1)
    {
        var result = await _adjustments.GetPagedAdjustmentsAsync(
            search,
            branchId,
            variantId,
            inventoryLotId,
            minimumQuantity,
            maximumQuantity,
            startDate,
            endDate,
            page);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [RequirePermission("stock.view")]
    public async Task<IActionResult> GetAdjustment(int id)
    {
        return Ok(await _adjustments.GetAdjustmentByIdAsync(id));
    }

    [HttpPost]
    [RequirePermission("stock.reduce")]
    public async Task<IActionResult> CreateAdjustment(
        [FromBody] CreateInventoryAdjustmentRequest request)
    {
        var result = await _adjustments.CreateAdjustmentAsync(request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:int}")]
    [RequirePermission("stock.reduce")]
    public async Task<IActionResult> UpdateAdjustment(
        int id,
        [FromBody] UpdateInventoryAdjustmentRequest request)
    {
        return Ok(await _adjustments.UpdateAdjustmentAsync(id, request));
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("stock.reduce")]
    public async Task<IActionResult> DeleteAdjustment(int id)
    {
        await _adjustments.DeleteAdjustmentAsync(id);
        return NoContent();
    }

    [HttpPut("reorder-level")]
    [RequirePermission("stock.reduce")]
    public async Task<IActionResult> UpdateReorderLevel(
        [FromBody] SetReorderLevelRequest request)
    {
        await _adjustments.UpdateReorderLevelAsync(request);
        return NoContent();
    }
}
