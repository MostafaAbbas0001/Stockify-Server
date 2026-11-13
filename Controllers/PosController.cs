namespace Stockify.Controllers;

[ApiController]
[Route("api/pos")]
[RequirePermission("pos.access")]
public class PosController : ControllerBase
{
    private readonly PosService _posService;
    private readonly CheckoutService _checkout;

    public PosController(PosService posService, CheckoutService checkout)
    {
        _posService = posService;
        _checkout = checkout;
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

    /// <summary>Sells a cart: order + payment in one transaction, safe to retry with the same key.</summary>
    [HttpPost("checkout")]
    [RequirePermission("order.create")]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequest dto)
    {
        if (dto.Payment != null && !User.HasPermission("invoice.payment.add"))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { code = "FORBIDDEN", message = "Recording a payment needs the invoice.payment.add permission." });

        var result = await _checkout.CheckoutAsync(dto, User.GetBranchId());
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Prices a cart the way the order will be priced, so the till never shows a different total.</summary>
    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] CartPreviewRequest dto)
    {
        return Ok(await _posService.PreviewAsync(dto));
    }
}

