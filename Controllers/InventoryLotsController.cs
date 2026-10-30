namespace Stockify.Controllers;

[ApiController]
[Route("api/inventory-lots")]
public class InventoryLotsController : ControllerBase
{
    private readonly InventoryLotService _service;

    public InventoryLotsController(InventoryLotService service)
    {
        _service = service;
    }

    [HttpGet("{id:int}")]
    [RequirePermission("stock.view")]
    public async Task<IActionResult> GetById(int id)
    {
        return Ok(await _service.GetLotByIdAsync(id));
    }

    [HttpPost]
    [RequirePermission("stock.restock")]
    public async Task<IActionResult> Create(
        [FromBody] CreateInventoryLotRequest request)
    {
        var lot = await _service.CreateLotAsync(
            request.VariantId,
            request.LotNumber,
            request.ManufacturingDate,
            request.ExpiryDate);

        return CreatedAtAction(
            nameof(GetById),
            new { id = lot.Id },
            await _service.GetLotByIdAsync(lot.Id));
    }

    [HttpGet]
    [RequirePermission("stock.view")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] int? productId,
        [FromQuery] int? variantId,
        [FromQuery] int? branchId,
        [FromQuery] bool includeEmpty = false,
        [FromQuery] bool includeExpired = true,
        [FromQuery] int page = 1)
    {
        return Ok(await _service.GetPagedLotsAsync(
            search,
            productId,
            variantId,
            branchId,
            includeEmpty,
            includeExpired,
            page));
    }
}
