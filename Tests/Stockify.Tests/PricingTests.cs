using Stockify.Models.Entities;
using Stockify.Services;

namespace Stockify.Tests;

/// <summary>Pure pricing rules (DECISIONS D7, D8, D17). No database.</summary>
public class PricingTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    private static Sale Sale(string typeCode, decimal value, bool deleted = false, DateTime? end = null) => new()
    {
        Name = "test",
        SaleType = new SaleType { Code = typeCode, Name = typeCode },
        Value = value,
        StartDate = Now.AddDays(-1),
        EndDate = end ?? Now.AddDays(1),
        IsDeleted = deleted,
    };

    [Fact]
    public void Percentage_sale_rounds_per_unit()
    {
        Assert.Equal(26.00m, Pricing.PerUnitSaleAmount(129.99m, Sale(SaleTypeCodes.Percentage, 20m), Now));
    }

    [Fact]
    public void Fixed_sale_is_capped_at_price()
    {
        Assert.Equal(0.40m, Pricing.PerUnitSaleAmount(0.40m, Sale(SaleTypeCodes.FixedAmount, 0.50m), Now));
    }

    [Fact]
    public void Deleted_or_expired_sale_is_inactive()
    {
        Assert.Equal(0m, Pricing.PerUnitSaleAmount(100m, Sale(SaleTypeCodes.Percentage, 20m, deleted: true), Now));
        Assert.Equal(0m, Pricing.PerUnitSaleAmount(100m, Sale(SaleTypeCodes.Percentage, 20m, end: Now.AddDays(-1)), Now));
        Assert.Equal(0m, Pricing.PerUnitSaleAmount(100m, null, Now));
    }

    [Fact]
    public void Order_discount_applies_to_net_and_is_capped()
    {
        var puffer = new Pricing.Line(1, 1, 129.99m, 26.00m);

        var amount = Pricing.OrderTotals(new[] { puffer }, DiscountTypeCodes.Amount, 10000m, 0.11m, 0m);
        Assert.Equal(103.99m, amount.DiscountAmount);
        Assert.Equal(0m, amount.TaxAmount);
        Assert.Equal(0m, amount.Total);

        var percent = Pricing.OrderTotals(new[] { puffer }, DiscountTypeCodes.Percent, 150m, 0.11m, 0m);
        Assert.Equal(103.99m, percent.DiscountAmount);
        Assert.Equal(0m, percent.Total);
    }

    [Fact]
    public void Totals_for_the_audit_cart()
    {
        // Puffer Jacket x2 (129.99, 20 % off) + a 40.00 item, order discount 10 %, VAT 11 %.
        var lines = new[] { new Pricing.Line(1, 2, 129.99m, 26.00m), new Pricing.Line(2, 1, 40.00m, 0m) };

        var totals = Pricing.OrderTotals(lines, DiscountTypeCodes.Percent, 10m, 0.11m, 0m);

        Assert.Equal(299.98m, totals.GrossSubtotal);
        Assert.Equal(52.00m, totals.SaleAmount);
        Assert.Equal(247.98m, totals.NetSubtotal);
        Assert.Equal(24.80m, totals.DiscountAmount);
        Assert.Equal(24.55m, totals.TaxAmount); // (247.98 - 24.80) * 0.11 = 24.5498
        Assert.Equal(247.73m, totals.Total);
    }

    [Fact]
    public void Free_item_has_no_tax_and_delivery_is_added()
    {
        var free = new Pricing.Line(1, 1, 0.40m, 0.40m);

        var totals = Pricing.OrderTotals(new[] { free }, DiscountTypeCodes.None, 0m, 0.11m, 3.00m);

        Assert.Equal(0m, totals.TaxAmount);
        Assert.Equal(3.00m, totals.Total);
    }

    [Fact]
    public void Line_values_are_rounded_once()
    {
        var line = new Pricing.Line(1, 3, 19.99m, 0m);
        Assert.Equal(59.97m, line.LineGross);
        Assert.Equal(59.97m, line.LineNet);
        Assert.Equal(6.60m, Pricing.OrderTotals(new[] { line }, DiscountTypeCodes.None, 0m, 0.11m, 0m).TaxAmount); // 6.5967
    }
}
