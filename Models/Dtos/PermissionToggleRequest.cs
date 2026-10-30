using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

public class PermissionToggleRequest
{
    [Required]
    public int PermissionId { get; set; }

    [Required]
    public bool IsActive { get; set; }
}
