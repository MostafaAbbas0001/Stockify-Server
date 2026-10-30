using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace Stockify.Controllers;

/// <summary>Platform operator API: sign-in and tenant (company) management.</summary>
[ApiController]
[Route("api/platform")]
[Authorize(Policy = AuthPolicies.Platform)]
public class PlatformController : ControllerBase
{
    private readonly PlatformAuthService _platformAuthService;
    private readonly TenantProvisioningService _provisioningService;

    public PlatformController(
        PlatformAuthService platformAuthService,
        TenantProvisioningService provisioningService)
    {
        _platformAuthService = platformAuthService;
        _provisioningService = provisioningService;
    }

    [HttpPost("auth/login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthPolicies.SignInRateLimit)]
    public async Task<IActionResult> Login([FromBody] LoginRequest creds)
    {
        return Ok(await _platformAuthService.LoginAsync(creds));
    }

    [HttpGet("tenants")]
    public async Task<IActionResult> GetTenants()
    {
        return Ok(await _provisioningService.GetTenantsAsync());
    }

    [HttpPost("tenants")]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest dto)
    {
        var result = await _provisioningService.CreateTenantAsync(dto);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPatch("tenants/{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] UpdateTenantStatusRequest dto)
    {
        return Ok(await _provisioningService.SetStatusAsync(id, dto.Status));
    }
}
