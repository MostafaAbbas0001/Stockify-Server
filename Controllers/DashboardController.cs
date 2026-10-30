namespace Stockify.Controllers;

[ApiController]
[Route("api/dashboard")]
[RequirePermission("dashboard.tab.view")]
public class DashboardController : ControllerBase
{
    private const string BranchAllAccess = "branch.all.access";
    private readonly DashboardService _dashboardService;

    public DashboardController(DashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// Users with branch.all.access may pick any branch (or all with null);
    /// everyone else is always limited to the branch in their token.
    /// </summary>
    private int? ResolveBranchScope(int? requestedBranchId) =>
        User.HasPermission(BranchAllAccess) ? requestedBranchId : User.GetBranchId();

    [HttpGet("stock-summary")]
    public async Task<ActionResult> GetDashboard
    (
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate,
        [FromQuery] int? branchId
    )
    {
        var (startUtc, endUtcExclusive) = DateRangeHelper.NormalizeToUtcRange(startDate, endDate);
        var result = await _dashboardService.GetStockSummaryAsync(
            startUtc, endUtcExclusive, ResolveBranchScope(branchId));
        return Ok(result);
    }

    [HttpGet("expenses-summary")]
    public async Task<ActionResult> GetExpensesSummary(
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate,
        [FromQuery] int? branchId)
    {
        var (startUtc, endUtcExclusive) = DateRangeHelper.NormalizeToUtcRange(startDate, endDate);
        var result = await _dashboardService.GetExpensesSummaryAsync(
            startUtc, endUtcExclusive, ResolveBranchScope(branchId));
        return Ok(result);
    }

    [HttpGet("total-revenue-summary")]
    public async Task<ActionResult> GetTotalRevenueSummary(
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate,
        [FromQuery] int? branchId)
    {
        var (startUtc, endUtcExclusive) = DateRangeHelper.NormalizeToUtcRange(startDate, endDate);
        var result = await _dashboardService.GetDailySalesSummaryAsync(
            startUtc, endUtcExclusive, ResolveBranchScope(branchId), User.HasPermission(BranchAllAccess));
        return Ok(result);
    }

    [HttpGet("top-products")]
    public async Task<ActionResult> GetTopProducts(
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate,
        [FromQuery] int? branchId)
    {
        var (startUtc, endUtcExclusive) = DateRangeHelper.NormalizeToUtcRange(startDate, endDate);
        var result = await _dashboardService.GetTopProductsAsync(
            startUtc, endUtcExclusive, ResolveBranchScope(branchId), User.HasPermission(BranchAllAccess));
        return Ok(result);
    }

    [HttpGet("stock-demand-trend")]
    public async Task<ActionResult> GetStockDemandTrend(
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate,
        [FromQuery] int? branchId,
        [FromQuery] int? productId)
    {
        var (startUtc, endUtcExclusive) = DateRangeHelper.NormalizeToUtcRange(startDate, endDate);
        var result = await _dashboardService.GetStockDemandTrendAsync(
            startUtc, endUtcExclusive, ResolveBranchScope(branchId), productId);
        return Ok(result);
    }
}
