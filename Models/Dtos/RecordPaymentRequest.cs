namespace Stockify.Models.Dtos;

public class RecordPaymentRequest
{
    /// <summary>Amount applied to the invoice, in <see cref="CurrencyId"/>; at least 0.01.</summary>
    public decimal Amount { get; set; }

    /// <summary>Cash handed over, when more than <see cref="Amount"/>; the response returns the change.</summary>
    public decimal? Tendered { get; set; }

    /// <summary>Id of a PaymentMethods row.</summary>
    public int PaymentMethodId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public int CurrencyId { get; set; }
}
