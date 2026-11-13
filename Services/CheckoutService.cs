using Npgsql;

namespace Stockify.Services;

/// <summary>
/// A till sale in one step (D20): places the order and records its payment in one serializable
/// transaction, so either both exist or neither does. A repeated idempotency key returns the sale the
/// key already produced, so retries and double-clicks never sell twice.
/// </summary>
public class CheckoutService
{
    private readonly AppDbContext _db;
    private readonly OrderService _orders;
    private readonly InvoiceService _invoices;

    public CheckoutService(AppDbContext db, OrderService orders, InvoiceService invoices)
    {
        _db = db;
        _orders = orders;
        _invoices = invoices;
    }

    public async Task<CheckoutResult> CheckoutAsync(CheckoutRequest dto, int branchId)
    {
        var key = dto.IdempotencyKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > 64)
            throw new ArgumentException("An idempotency key (up to 64 characters) is required.");

        if (await FindAsync(key) is { } earlier)
            return earlier;

        try
        {
            return await SerializableTransactions.RunAsync(_db, async () =>
            {
                if (await FindAsync(key) is { } concurrent)
                    return concurrent;

                var placed = await _orders.PlaceOrderAsync(dto, branchId);
                PaymentResult? paid = null;
                // Nothing is due on a free sale: it is already settled, so no payment is recorded.
                if (dto.Payment is { } payment && placed.Total > 0m)
                    paid = await _invoices.AddPaymentAsync(placed.InvoiceId, await PaymentFor(placed, payment));

                _db.CheckoutKeys.Add(new CheckoutKey
                {
                    Key = key,
                    OrderId = placed.OrderId,
                    InvoiceId = placed.InvoiceId,
                    Change = paid?.Change ?? 0m,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();

                return new CheckoutResult(
                    placed.OrderId,
                    placed.InvoiceId,
                    placed.InvoiceNumber,
                    placed.Total,
                    paid?.PaidAmountBase ?? 0m,
                    paid?.FullyPaid ?? placed.Total == 0m,
                    paid?.Change ?? 0m);
            });
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request with the same key committed first: answer with its sale.
            _db.ChangeTracker.Clear();
            return await FindAsync(key) ?? throw new InvalidOperationException("Checkout conflict; please retry.");
        }
    }

    /// <summary>Turns "the customer handed over X" into the amount applied to the invoice.</summary>
    private async Task<RecordPaymentRequest> PaymentFor(PlacedOrder placed, CheckoutPayment payment)
    {
        var rate = await _db.Currencies
            .Where(currency => currency.Id == payment.CurrencyId && currency.IsActive)
            .Select(currency => (decimal?)currency.ExchangeRate)
            .FirstOrDefaultAsync()
            ?? throw new ArgumentException("Invalid or inactive currency.");

        // The total in the payment currency; a cash payment can cover it, part of it, or more (change).
        var due = Pricing.Round2(placed.Total * rate);
        var amount = payment.Tendered is decimal tendered ? Math.Min(tendered, due) : due;

        return new RecordPaymentRequest
        {
            Amount = amount,
            Tendered = payment.Tendered,
            PaymentMethodId = payment.PaymentMethodId,
            CurrencyId = payment.CurrencyId,
            Reference = payment.Reference
        };
    }

    private async Task<CheckoutResult?> FindAsync(string key)
    {
        var row = await _db.CheckoutKeys
            .AsNoTracking()
            .Where(k => k.Key == key)
            .Select(k => new
            {
                k.OrderId,
                k.InvoiceId,
                k.Change,
                k.Order.Invoices.First(i => i.Id == k.InvoiceId).InvoiceNumber,
                Total = k.Order.Invoices.First(i => i.Id == k.InvoiceId).TotalAmount,
                StatusCode = k.Order.Invoices.First(i => i.Id == k.InvoiceId).InvoiceStatus.Code,
                Paid = _db.Payments
                    .Where(p => p.InvoiceId == k.InvoiceId)
                    .Sum(p => (decimal?)(p.Amount / p.Currency.ExchangeRate)) ?? 0m
            })
            .FirstOrDefaultAsync();

        return row == null
            ? null
            : new CheckoutResult(row.OrderId, row.InvoiceId, row.InvoiceNumber, row.Total,
                Pricing.Round2(row.Paid), row.StatusCode == InvoiceStatusCodes.FullyPaid, row.Change);
    }
}
