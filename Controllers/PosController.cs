namespace Stockify.Controllers;

[ApiController]
[Route("api/pos")]
[RequirePermission("pos.access")]
public class PosController : ControllerBase
{
    private readonly PosService _posService;

    public PosController(PosService posService)
    {
        _posService = posService;
    }

    [HttpGet("items")]
    public async Task<IActionResult> Scan([FromQuery] string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return BadRequest(new { code = "BAD_REQUEST", message = "Barcode or SKU required" });

        var branchId = User.GetBranchId();

        var result = await _posService.ScanAsync(code, branchId);

        return Ok(result);
    }
}

