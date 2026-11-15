namespace Stockify.Controllers;

/// <summary>The signed-in user's own account. Any tenant user may use it; no permission needed.</summary>
[ApiController]
[Route("api/account")]
public class AccountController : ControllerBase
{
    private readonly AuthService _authService;

    public AccountController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        await _authService.ChangePasswordAsync(User.GetUserId(), request);
        return NoContent();
    }
}
