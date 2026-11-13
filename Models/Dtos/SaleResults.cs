namespace Stockify.Models.Dtos;

/// <summary>What POST /api/orders returns.</summary>
public sealed record PlacedOrder(int OrderId, int InvoiceId, string InvoiceNumber, decimal Total);

/// <summary>What POST /api/invoices/{id}/payments returns. Amounts are in base currency except Change.</summary>
public sealed record PaymentResult(
    int InvoiceId,
    string InvoiceNumber,
    decimal PaidAmountBase,
    decimal RemainingBase,
    bool FullyPaid,
    decimal Change);

/// <summary>What POST /api/pos/checkout returns; the same body is returned again for a repeated key.</summary>
public sealed record CheckoutResult(
    int OrderId,
    int InvoiceId,
    string InvoiceNumber,
    decimal Total,
    decimal PaidAmountBase,
    bool FullyPaid,
    decimal Change);
