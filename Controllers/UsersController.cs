namespace Stockify.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;

    public UsersController(UserService userService)
    {
        _userService = userService;
    }

    [HttpPut("{id:int}")]
    [RequirePermission("users.update")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest dto)
    {
        var result = await _userService.UpdateUserAsync(id, dto);
        return Ok(result);
    }

    [HttpPost]
    [RequirePermission("users.create")]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest dto)
    {
        var result = await _userService.CreateUserAsync(dto);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet]
    [RequirePermission("users.view")]
    public async Task<IActionResult> GetAllUsers(
        [FromQuery] string? search,
        [FromQuery] int? roleId,
        [FromQuery] int? branchId,
        [FromQuery] int page = 1)
    {
        var users = await _userService.GetAllUsersAsync(search, roleId, branchId, page);
        return Ok(users);
    }

    [HttpGet("{id:int}")]
    [RequirePermission("users.view")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _userService.GetByIdAsync(id);
        return Ok(result);
    }

    [HttpGet("roles")]
    [RequirePermission("users.view")]
    public async Task<IActionResult> GetRoles()
    {
        var roles = await _userService.GetRolesAsync();
        return Ok(roles);
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("users.delete")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        await _userService.DeleteUserAsync(id, User.GetUserId());
        return NoContent();
    }

    [HttpGet("{id:int}/permissions")]
    [RequirePermission("users.view")]
    public async Task<IActionResult> GetUserPermissions(int id)
    {
        var result = await _userService.GetUserPermissionsAsync(id);
        return Ok(result);
    }

    [HttpPut("{id:int}/permissions")]
    [RequirePermission("users.update")]
    public async Task<IActionResult> UpdateUserPermissions(
        int id,
        [FromBody] UpdateUserPermissionsRequest dto)
    {
        await _userService.UpdateUserPermissionsAsync(id, dto, User.GetUserId());
        return NoContent();
    }
}
