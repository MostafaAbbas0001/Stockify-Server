namespace Stockify.Models.Dtos;

public class RecordPaymentRequest
{
    public decimal Amount { get; set; }
    /// <summary>Id of a PaymentMethods row.</summary>
    public int PaymentMethodId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public int CurrencyId { get; set; }
}
