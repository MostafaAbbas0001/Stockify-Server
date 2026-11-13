namespace Stockify.Controllers;

[ApiController]
[Route("api/reports")]
[RequirePermission("reports.tab.view")]
public class ReportsController : ControllerBase
{
    private readonly ReportService _reportService;
    private readonly EndOfDayService _endOfDay;

    public ReportsController(ReportService reportService, EndOfDayService endOfDay)
    {
        _reportService = reportService;
        _endOfDay = endOfDay;
    }

    /// <summary>End-of-day (Z) summary for one day: sales, payments by method, refunds, cancels, per seller.</summary>
    [HttpGet("end-of-day")]
    public async Task<IActionResult> EndOfDay([FromQuery] DateTime? date, [FromQuery] int? branchId)
    {
        return Ok(await _endOfDay.GetAsync(date ?? DateTime.UtcNow, User.BranchScope(branchId)));
    }

    private FileContentResult ToFile(ReportFile report) =>
        File(report.Content, report.ContentType, report.FileName);

    [HttpGet("stock-on-hand")]
    public async Task<IActionResult> ExportStockOnHandXlsx(CancellationToken ct = default)
    {
        var result = await _reportService.ExportStockOnHandAsync(ct);
        return ToFile(result);
    }

    [HttpGet("low-stock")]
    public async Task<IActionResult> ExportLowStock(CancellationToken ct = default)
    {
        var result = await _reportService.ExportLowStockAsync(ct);
        return ToFile(result);
    }

    [HttpGet("daily-sales")]
    public async Task<IActionResult> ExportDailySales([FromQuery] DateTime date, [FromQuery] int? branchId, CancellationToken ct = default)
    {
        var result = await _reportService.ExportDailySalesAsync(date, User.BranchScope(branchId), ct);

        return ToFile(result);
    }

    [HttpGet("monthly-sales")]
    public async Task<IActionResult> ExportMonthlySales([FromQuery] DateTime startDate, [FromQuery] DateTime? endDate, [FromQuery] int? branchId, CancellationToken ct = default)
    {
        var result = await _reportService.ExportMonthlySalesAsync(startDate, endDate, User.BranchScope(branchId), ct);

        return ToFile(result);
    }

    [HttpGet("branch-performance")]
    public async Task<IActionResult> ExportBranchPerformance([FromQuery] DateTime startDate, [FromQuery] DateTime? endDate, [FromQuery] int branchId, CancellationToken ct = default)
    {
        var result = await _reportService.ExportBranchPerformanceAsync(startDate, endDate, User.BranchScope(branchId) ?? branchId, ct);

        return ToFile(result);
    }

    [HttpGet("staff-performance")]
    public async Task<IActionResult> ExportStaffPerformance([FromQuery] DateTime startDate, [FromQuery] DateTime? endDate, [FromQuery] int employeeId, CancellationToken ct = default)
    {
        var result = await _reportService.ExportStaffPerformanceAsync(startDate, endDate, employeeId, ct);

        return ToFile(result);
    }

    [HttpGet("orders")]
    public async Task<IActionResult> ExportOrders([FromQuery] DateTime startDate, [FromQuery] DateTime? endDate, CancellationToken ct = default)
    {
        var result = await _reportService.ExportOrdersAsync(startDate, endDate, ct);

        return ToFile(result);
    }

    [HttpGet("deliveries")]
    public async Task<IActionResult> ExportDelivery([FromQuery] int? deliveryLocationId, CancellationToken ct = default)
    {
        var result = await _reportService.ExportDeliveryAsync(deliveryLocationId, ct);
        return ToFile(result);
    }

    [HttpGet("customers")]
    public async Task<IActionResult> ExportCustomers([FromQuery] DateTime startDate, [FromQuery] DateTime? endDate, [FromQuery] int? customerId, CancellationToken ct = default)
    {
        var result = await _reportService.ExportCustomersAsync(startDate, endDate, customerId, ct);

        return ToFile(result);
    }

    [HttpGet("expenses")]
    public async Task<IActionResult> ExportExpenses(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? categoryId,
        [FromQuery] int? branchId,
        CancellationToken ct = default)
    {
        var result = await _reportService.ExportExpensesAsync(from, to, categoryId, User.BranchScope(branchId), ct);
        return ToFile(result);
    }
}

