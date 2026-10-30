namespace Stockify.Controllers;

[ApiController]
[Route("api/tax-rates")]
public class TaxRateController : ControllerBase
{
    private readonly TaxService _taxService;
    public TaxRateController(TaxService taxService) => _taxService = taxService;


    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await _taxService.GetAllAsync();
        return Ok(result);
    }

}
