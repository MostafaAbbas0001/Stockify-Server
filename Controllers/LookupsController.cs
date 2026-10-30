namespace Stockify.Controllers;

/// <summary>Every lookup list (statuses, types, methods) the UI needs for selects and badges.</summary>
[ApiController]
[Route("api/lookups")]
public class LookupsController : ControllerBase
{
    private readonly LookupService _lookups;

    public LookupsController(LookupService lookups)
    {
        _lookups = lookups;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _lookups.GetAllAsync());
    }
}
