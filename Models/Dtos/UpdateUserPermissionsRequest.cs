using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

public class UpdateUserPermissionsRequest
{
    [Required]
    public List<PermissionToggleRequest> Items { get; set; } = new();
}
