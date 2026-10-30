namespace Stockify.Controllers;

[ApiController]
[Route("api/stock-movements")]
public class StockMovementsController : ControllerBase
{
    private readonly StockMovementService _stockMovementService;
    public StockMovementsController(StockMovementService stockMovementService) => _stockMovementService = stockMovementService;

    [HttpGet]
    [RequirePermission("stockMovement.view")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] int? movementTypeId = null,
        [FromQuery] int? branchId = null,
        [FromQuery] int? variantId = null,
        [FromQuery] int? inventoryLotId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int page = 1)
    {
        var result = await _stockMovementService.GetAllAsync(
            search,
            movementTypeId,
            branchId,
            variantId,
            inventoryLotId,
            startDate,
            endDate,
            page);
        return Ok(result);
    }

    // GET: api/stock-movements/types
    [HttpGet("types")]
    [RequirePermission("stockMovement.view")]
    public async Task<IActionResult> GetMovementTypes()
    {
        var types = await _stockMovementService.GetMovementTypesAsync();
        return Ok(types);
    }
}
