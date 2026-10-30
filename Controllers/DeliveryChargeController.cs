namespace Stockify.Controllers;

[ApiController]
[Route("api/delivery-charges")]
public class DeliveryChargeController : ControllerBase
{
    private readonly DeliveryLocationService _service;

    public DeliveryChargeController(DeliveryLocationService service)
    {
        _service = service;
    }

    [HttpPost]
    [RequirePermission("deliveryCharge.create")]
    public async Task<IActionResult> Create([FromBody] DeliveryLocationRequest dto)
    {
        var result = await _service.CreateAsync(
            new DeliveryLocation { LocationName = dto.LocationName, Fee = dto.Fee });
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet]
    [RequirePermission("deliveryCharge.view")]
    public async Task<IActionResult> GetAll()
    {
        var locationDeliveryCharges = await _service.GetAllAsync();

        return Ok(locationDeliveryCharges);
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("deliveryCharge.delete")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }

    [HttpPut("{id:int}")]
    [RequirePermission("deliveryCharge.update")]
    public async Task<IActionResult> Update(int id, [FromBody] DeliveryLocationRequest dto)
    {
        var result = await _service.UpdateAsync(
            id, new DeliveryLocation { LocationName = dto.LocationName, Fee = dto.Fee });
        return Ok(result);
    }
}
