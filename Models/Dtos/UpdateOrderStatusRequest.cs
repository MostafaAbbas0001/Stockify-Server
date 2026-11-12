namespace Stockify.Models.Dtos;

public class UpdateOrderStatusRequest
{
    /// <summary>ship, deliver or cancel.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Why the order is cancelled; stored on the order and on its stock movements.</summary>
    public string? Reason { get; set; }
}
