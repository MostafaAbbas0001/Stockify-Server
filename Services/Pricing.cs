namespace Stockify.Services;

/// <summary>
/// The one place that prices a sale. Used by order placement, the POS preview and checkout, so a
/// cart, its order and its invoice can never disagree. Rules (docs/DECISIONS.md D7, D8, D17):
/// money is rounded once per line to 2 decimals; a promotion is a per-unit amount capped at the price;
/// the order discount applies to the net subtotal (after promotions) and is capped at it; VAT is
/// charged on what the customer actually pays; the total is never negative.
/// </summary>
public static class Pricing
{
    public static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static bool SaleIsActive(Sale? sale, DateTime nowUtc) =>
        sale is { IsDeleted: false } &&
        (!sale.StartDate.HasValue || sale.StartDate <= nowUtc) &&
        (!sale.EndDate.HasValue || nowUtc <= sale.EndDate);

    /// <summary>Promotion discount for one unit; needs <c>sale.SaleType</c> loaded.</summary>
    public static decimal PerUnitSaleAmount(decimal price, Sale? sale, DateTime nowUtc)
    {
        if (!SaleIsActive(sale, nowUtc))
            return 0m;

        var amount = sale!.SaleType.Code == SaleTypeCodes.Percentage
            ? Round2(price * sale.Value / 100m)
            : Round2(sale.Value);
        return Math.Min(Math.Max(amount, 0m), price);
    }

    /// <summary>One priced line of a cart or order.</summary>
    public sealed record Line(int VariantId, int Quantity, decimal BaseUnitPrice, decimal PerUnitSaleAmount)
    {
        public decimal FinalUnitPrice => Round2(BaseUnitPrice - PerUnitSaleAmount);
        public decimal LineGross => Round2(BaseUnitPrice * Quantity);
        public decimal LineSale => Round2(PerUnitSaleAmount * Quantity);
        public decimal LineNet => Round2(FinalUnitPrice * Quantity);
    }

    public sealed record Totals(
        decimal GrossSubtotal,
        decimal SaleAmount,
        decimal NetSubtotal,
        decimal DiscountAmount,
        decimal TaxAmount,
        decimal DeliveryFee,
        decimal Total);

    /// <param name="discountTypeCode">A DiscountTypes code: NONE, PERCENT or AMOUNT.</param>
    /// <param name="taxRate">Fraction, e.g. 0.11 for 11 %.</param>
    public static Totals OrderTotals(
        IReadOnlyList<Line> lines,
        string discountTypeCode,
        decimal discountValue,
        decimal taxRate,
        decimal deliveryFee)
    {
        var gross = Round2(lines.Sum(line => line.LineGross));
        var sale = Round2(lines.Sum(line => line.LineSale));
        var net = Round2(gross - sale);

        var discount = discountTypeCode switch
        {
            DiscountTypeCodes.Percent => Round2(net * Math.Min(Math.Max(discountValue, 0m), 100m) / 100m),
            DiscountTypeCodes.Amount => Math.Min(Math.Max(discountValue, 0m), net),
            _ => 0m
        };
        discount = Math.Min(Round2(discount), net);

        var taxable = net - discount;
        var tax = Round2(taxable * taxRate);
        var total = Math.Max(0m, Round2(taxable + tax + deliveryFee));

        return new Totals(gross, sale, net, discount, tax, deliveryFee, total);
    }
}
