namespace Stockify.Controllers;


[ApiController]
[Route("api/invoices")]
public class InvoicesController : ControllerBase
{
    private readonly InvoiceService _invoiceService;

    public InvoicesController(InvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    [HttpGet]
    [RequirePermission("invoice.view")]
    public async Task<IActionResult> GetAllInvoices(
        [FromQuery] string? search = null,
        [FromQuery] int? statusId = null,
        [FromQuery] int? customerId = null,
        [FromQuery] int? orderId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int page = 1)
    {
        var result = await _invoiceService.GetAllInvoicesAsync(
            search,
            statusId,
            customerId,
            orderId,
            startDate,
            endDate,
            page);
        return Ok(result);
    }

    [HttpGet("{invoiceId:int}")]
    [RequirePermission("invoice.view")]
    public async Task<IActionResult> GetInvoiceById(int invoiceId)
    {
        var result = await _invoiceService.GetInvoiceByIdAsync(invoiceId);

        return Ok(result);
    }

      [HttpGet("statuses")]
      [RequirePermission("invoice.view")]
      public async Task<IActionResult> GetInvoiceStatuses()
      {
        var result = await _invoiceService.GetInvoiceStatusesAsync();
        return Ok(result);
      }

      [HttpPost("{invoiceId:int}/payments")]
      [RequirePermission("invoice.payment.add")]
      public async Task<IActionResult> AddPayment(
          int invoiceId,
          RecordPaymentRequest recordPaymentDto)
      {
        var result = await _invoiceService.AddPaymentAsync(invoiceId, recordPaymentDto);

        return StatusCode(StatusCodes.Status201Created, result);
      }

    [HttpPost("{invoiceId:int}/returns")]
    [RequirePermission("invoice.create")]
    public async Task<IActionResult> ReturnInvoice(
        int invoiceId,
        [FromBody] CreateInvoiceReturnRequest req)
    {
        var result = await _invoiceService.ReturnInvoiceAsync(invoiceId, req);

        return StatusCode(201, result);
    }
}
