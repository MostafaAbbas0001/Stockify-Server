namespace Stockify.Models.Entities;

public class Currency : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Symbol { get; set; }
    public decimal ExchangeRate { get; set; }
    public bool IsActive { get; set; } = true;
}
