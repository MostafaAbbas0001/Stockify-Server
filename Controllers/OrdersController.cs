namespace Stockify.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly OrderService _orderService;

    public OrdersController(OrderService orderService)
    { 
        _orderService = orderService;
    }

    [HttpPost]
    [RequirePermission("order.create")]
    public async Task<IActionResult> PlaceOrder([FromBody] CreateOrderRequest dto)
    {
        var branchId = User.GetBranchId();
        var result = await _orderService.PlaceOrderAsync(dto, branchId);

        return StatusCode(StatusCodes.Status201Created, result);
    }


    [HttpPatch("{orderId:int}/status")]
    [RequirePermission("order.view")]
    public async Task<IActionResult> UpdateOrderStatus(
        int orderId,
        [FromBody] UpdateOrderStatusRequest request)
    {
        var result = await _orderService.UpdateOrderStatusAsync(orderId, request);

        return Ok(result);
    }

    [HttpGet("statuses")]
    [RequirePermission("order.view")]
    public async Task<IActionResult> GetOrderStatuses()
    {
        var result = await _orderService.GetOrderStatusesAsync();
        return Ok(result);
    }

    [HttpGet]
    [RequirePermission("order.view")]
    public async Task<IActionResult> GetAllOrders(
        [FromQuery] string? search = null,
        [FromQuery] int? statusId = null,
        [FromQuery] int? customerId = null,
        [FromQuery] int? branchId = null,
        [FromQuery] int? deliveryLocationId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] bool forDeliveryOnly = false,
        [FromQuery] int page = 1)
    {
        var result = await _orderService.GetAllOrdersAsync(
            search,
            statusId,
            customerId,
            branchId,
            deliveryLocationId,
            startDate,
            endDate,
            forDeliveryOnly,
            page);
        return Ok(result);
    }

    [HttpGet("{orderId:int}")]
    [RequirePermission("order.view")]
    public async Task<IActionResult> GetOrderDetails(int orderId)
    {
        var result = await _orderService.GetOrderDetailsAsync(orderId);

        return Ok(result);
    }
}

