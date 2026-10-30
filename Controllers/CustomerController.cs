namespace Stockify.Controllers;

[Route("api/customers")]
[ApiController]
public class CustomerController : ControllerBase
{
    private readonly CustomerService _customerService;

    public CustomerController(CustomerService customerService)
    {
        _customerService = customerService;
    }

    [HttpGet]
    [RequirePermission("customer.view")]
    public async Task<IActionResult> GetCustomers(
        [FromQuery] string? search,
        [FromQuery] int page = 1)
    {
        var customers = await _customerService.GetAllAsync(search, page);
        return Ok(customers);
    }

    [HttpGet("{id:int}")]
    [RequirePermission("customer.view")]
    public async Task<ActionResult<Customer>> GetCustomer(int id)
    {
        var result = await _customerService.GetByIdAsync(id);
        return Ok(result);
    }

    [HttpPut("{id:int}")]
    [RequirePermission("customer.update")]
    public async Task<IActionResult> PutCustomer(int id, UpdateCustomerRequest customerDto)
    {
        var result = await _customerService.UpdateAsync(id, customerDto);
        return Ok(result);
    }

    [HttpPost]
    [RequirePermission("customer.create")]
    public async Task<ActionResult<Customer>> PostCustomer(CreateCustomerRequest customerDto)
    {
        var result = await _customerService.CreateAsync(customerDto);
        return CreatedAtAction("GetCustomer", new { id = result!.Id }, result);
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("customer.delete")]
    public async Task<IActionResult> DeleteCustomer(int id)
    {
        await _customerService.DeleteAsync(id);
        return NoContent();
    }
}
