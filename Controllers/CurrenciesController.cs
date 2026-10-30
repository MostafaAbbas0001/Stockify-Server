namespace Stockify.Controllers;

[ApiController]
[Route("api/currencies")]
public class CurrenciesController : ControllerBase
{
    private readonly CurrencyService _currencyService;

    public CurrenciesController(CurrencyService currencyService)
    {
        _currencyService = currencyService;
    }

    [HttpGet]
    [RequirePermission("currency.view")]
    public async Task<IActionResult> GetAll()
    {
        var currencies = await _currencyService.GetAllAsync();

        return Ok(currencies);
    }
}
