using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

/// <summary>A signed-in user changing their own password (POST /api/account/password).</summary>
public class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = null!;

    [Required]
    [MinLength(8)]
    public string NewPassword { get; set; } = null!;
}
