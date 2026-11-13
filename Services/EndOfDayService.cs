namespace Stockify.Services;

/// <summary>
/// The end-of-day (Z) summary: what was sold, how it was paid, what was refunded or cancelled, and who
/// sold it, for one day at one branch (or all branches). Amounts are in base currency. The day is a UTC
/// day until tenants get a time zone (backlog #32).
/// </summary>
public class EndOfDayService
{
    private readonly AppDbContext _db;

    public EndOfDayService(AppDbContext db)
    {
        _db = db;
    }

    public sealed record PaymentTotal(string MethodCode, string MethodName, int Count, decimal Amount);

    public sealed record EmployeeTotal(string Employee, int SalesCount, decimal Total);

    public sealed record Summary(
        DateTime Date,
        int? BranchId,
        int SalesCount,
        decimal NetSales,
        decimal Tax,
        decimal Delivery,
        decimal Total,
        IReadOnlyList<PaymentTotal> Payments,
        decimal Refunds,
        int RefundCount,
        int CancelledCount,
        decimal NetTakings,
        IReadOnlyList<EmployeeTotal> ByEmployee);

    public async Task<Summary> GetAsync(DateTime date, int? branchId)
    {
        var start = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        var end = start.AddDays(1);

        var sales = _db.Invoices
            .Where(i => i.IssuedAt >= start && i.IssuedAt < end)
            .Where(i => i.InvoiceStatus.Code != InvoiceStatusCodes.Void)
            .Where(i => branchId == null || i.Order.BranchId == branchId);

        var totals = await sales
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Net = g.Sum(i => i.Subtotal - i.SaleAmount - i.DiscountAmount),
                Tax = g.Sum(i => i.TaxAmount),
                Delivery = g.Sum(i => i.DeliveryChargeAmount),
                Total = g.Sum(i => i.TotalAmount)
            })
            .FirstOrDefaultAsync();

        // Money taken today, whenever the sale was made (a delivery paid on arrival counts today).
        var payments = (await _db.Payments
                .Where(p => p.PaidAt >= start && p.PaidAt < end)
                .Where(p => branchId == null || p.Invoice.Order.BranchId == branchId)
                .GroupBy(p => new { p.PaymentMethod.Code, p.PaymentMethod.Name })
                .Select(g => new
                {
                    g.Key.Code,
                    g.Key.Name,
                    Count = g.Count(),
                    Amount = g.Sum(p => p.Amount / p.Currency.ExchangeRate)
                })
                .ToListAsync())
            .Select(p => new PaymentTotal(p.Code, p.Name, p.Count, Pricing.Round2(p.Amount)))
            .OrderByDescending(p => p.Amount)
            .ToList();

        var refunds = await _db.CreditMemos
            .Where(c => c.CreatedAt >= start && c.CreatedAt < end)
            .Where(c => branchId == null || c.Invoice.Order.BranchId == branchId)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Amount = g.Sum(c => c.TotalAmount) })
            .FirstOrDefaultAsync();

        var cancelledCount = await _db.Orders
            .Where(o => o.CancelledAt >= start && o.CancelledAt < end)
            .Where(o => branchId == null || o.BranchId == branchId)
            .CountAsync();

        var byEmployee = (await sales
                .GroupBy(i => i.Order.CreatedByEmployee != null ? i.Order.CreatedByEmployee.Name : "—")
                .Select(g => new { Employee = g.Key, Count = g.Count(), Total = g.Sum(i => i.TotalAmount) })
                .ToListAsync())
            .Select(e => new EmployeeTotal(e.Employee, e.Count, e.Total))
            .OrderByDescending(e => e.Total)
            .ToList();

        var refundAmount = refunds?.Amount ?? 0m;
        return new Summary(
            start,
            branchId,
            totals?.Count ?? 0,
            totals?.Net ?? 0m,
            totals?.Tax ?? 0m,
            totals?.Delivery ?? 0m,
            totals?.Total ?? 0m,
            payments,
            refundAmount,
            refunds?.Count ?? 0,
            cancelledCount,
            Pricing.Round2(payments.Sum(p => p.Amount) - refundAmount),
            byEmployee);
    }
}
