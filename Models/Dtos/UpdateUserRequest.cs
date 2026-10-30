using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

public class UpdateUserRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = null!;

    [Required]
    [MinLength(3)]
    public string Username { get; set; } = null!;

    [Required]
    public int RoleId { get; set; }

    [Required]
    public int BranchId { get; set; }

    [MinLength(8)]
    public string? Password { get; set; }
}
