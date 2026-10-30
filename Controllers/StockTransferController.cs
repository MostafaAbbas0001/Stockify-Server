namespace Stockify.Controllers;

[ApiController]
[Route("api/stock-transfers")]
public class StockTransferController : ControllerBase
{
    private readonly StockTransferService _stockTransferService;

    public StockTransferController(StockTransferService stockTransferService)
    {
        _stockTransferService = stockTransferService;
    }

    [HttpPost]
    [RequirePermission("stockTransfer.request")]
    public async Task<IActionResult> CreateTransfer(
        [FromBody] CreateStockTransferRequest dto)
    {
        var userId = User.GetUserId();
        var result = await _stockTransferService.CreateTransferAsync(dto, userId);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("{id:int}/dispatches")]
    [RequirePermission("stockTransfer.send")]
    public async Task<IActionResult> DispatchTransfer(
        int id,
        [FromBody] DispatchStockTransferRequest dto)
    {
        var result = await _stockTransferService.DispatchTransferAsync(id, dto);

        return Ok(result);
    }

    [HttpPost("{id:int}/receipts")]
    [RequirePermission("stockTransfer.receive")]
    public async Task<IActionResult> RecordReceipt(
        int id,
        [FromBody] ReceiveStockTransferRequest dto)
    {
        var result = await _stockTransferService.RecordTransferReceiptAsync(
            id,
            dto);

        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("stockTransfer.cancel")]
    public async Task<IActionResult> CancelTransfer(int id, [FromQuery] string? reason)
    {
        await _stockTransferService.CancelTransferAsync(id, reason);
        return NoContent();
    }

    [HttpGet("{id:int}")]
    [RequirePermission("stockTransfer.view")]
    public async Task<IActionResult> GetTransferById(int id)
    {
        var result = await _stockTransferService.GetTransferByIdAsync(id);

        return Ok(result);
    }

    [HttpGet]
    [RequirePermission("stockTransfer.view")]
    public async Task<IActionResult> GetPagedTransfers(
        [FromQuery] string? search = null,
        [FromQuery] int? branchId = null,
        [FromQuery] int? sourceBranchId = null,
        [FromQuery] int? destinationBranchId = null,
        [FromQuery] int? statusId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int page = 1)
    {
        var result = await _stockTransferService.GetPagedTransfersAsync(
            search,
            branchId,
            sourceBranchId,
            destinationBranchId,
            statusId,
            startDate,
            endDate,
            page);
        return Ok(result);
    }

    [HttpGet("statistics")]
    [RequirePermission("stockTransfer.view")]
    public async Task<IActionResult> GetTransferStatistics([FromQuery] int? branchId = null)
    {
        var result = await _stockTransferService.GetTransferStatisticsAsync(branchId);
        return Ok(result);
    }

    [HttpPut("{id:int}/discrepancy")]
    [RequirePermission("stockTransfer.resolve")]
    public async Task<IActionResult> ResolveTransferDiscrepancy(
        int id,
        [FromBody] ResolveStockTransferDiscrepancyRequest dto)
    {
        var result = await _stockTransferService
            .ResolveTransferDiscrepancyAsync(id, dto);

        return Ok(result);
    }
}
