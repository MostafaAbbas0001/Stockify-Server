namespace Stockify.Models.Dtos;

public class UpdateOrderStatusRequest
{
    /// <summary>ship or deliver (cancelling is POST /api/orders/{id}/cancel).</summary>
    public string Action { get; set; } = string.Empty;
}
