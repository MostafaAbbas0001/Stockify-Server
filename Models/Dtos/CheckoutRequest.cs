using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

/// <summary>
/// One till sale: the order and (optionally) its payment, created together or not at all.
/// The client generates <see cref="IdempotencyKey"/> once per sale and resends it on retries.
/// </summary>
public class CheckoutRequest : CreateOrderRequest
{
    [Required]
    [MaxLength(64)]
    public string IdempotencyKey { get; set; } = null!;

    /// <summary>Null records an unpaid sale (DRAFT order, ISSUED invoice).</summary>
    public CheckoutPayment? Payment { get; set; }
}

public class CheckoutPayment
{
    /// <summary>Id of a PaymentMethods row.</summary>
    public int PaymentMethodId { get; set; }

    public int CurrencyId { get; set; }

    /// <summary>
    /// Money handed over, in the payment currency. Null pays the total exactly; less than the total
    /// records a partial payment; more returns change.
    /// </summary>
    public decimal? Tendered { get; set; }

    [MaxLength(100)]
    public string Reference { get; set; } = string.Empty;
}
