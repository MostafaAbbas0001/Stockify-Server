using Microsoft.EntityFrameworkCore;

namespace Stockify.Data;

/// <summary>
/// The rows of every lookup table. Codes come from Models/Entities/LookupCodes.cs;
/// names are the English defaults (the client translates by code) and tones colour the badges.
/// </summary>
public static class LookupSeeds
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var rows in Lists())
            modelBuilder.Entity(rows[0].GetType()).HasData(rows);
    }

    /// <summary>
    /// Every lookup list (one array per table), built fresh on each call. Used by the migrations (HasData)
    /// and by StartupSeeder, which inserts any row missing from the database when the API starts.
    /// </summary>
    public static IEnumerable<LookupEntity[]> Lists()
    {
        yield return Of(
            Row<OrderStatus>(1, OrderStatusCodes.Draft, "Draft", "neutral"),
            Row<OrderStatus>(2, OrderStatusCodes.InTransit, "In Transit", "info"),
            Row<OrderStatus>(3, OrderStatusCodes.Delivered, "Delivered", "primary"),
            Row<OrderStatus>(4, OrderStatusCodes.FullyPaid, "Fully Paid", "success"),
            Row<OrderStatus>(5, OrderStatusCodes.PartiallyPaid, "Partially Paid", "warning"),
            Row<OrderStatus>(6, OrderStatusCodes.Returned, "Returned", "error"),
            Row<OrderStatus>(7, OrderStatusCodes.PartiallyReturned, "Partially Returned", "warning"),
            Row<OrderStatus>(8, OrderStatusCodes.Cancelled, "Cancelled", "error"));

        yield return Of(
            Row<OrderType>(1, OrderTypeCodes.Delivery, "Delivery", "info"),
            Row<OrderType>(2, OrderTypeCodes.Pos, "POS", "primary"));

        yield return Of(
            Row<InvoiceStatus>(1, InvoiceStatusCodes.Issued, "Issued", "neutral"),
            Row<InvoiceStatus>(2, InvoiceStatusCodes.PartiallyPaid, "Partially Paid", "warning"),
            Row<InvoiceStatus>(3, InvoiceStatusCodes.FullyPaid, "Fully Paid", "success"),
            Row<InvoiceStatus>(4, InvoiceStatusCodes.Void, "Void", "error"),
            Row<InvoiceStatus>(5, InvoiceStatusCodes.WrittenOff, "Written Off", "error"),
            Row<InvoiceStatus>(6, InvoiceStatusCodes.Refunded, "Refunded", "error"),
            Row<InvoiceStatus>(7, InvoiceStatusCodes.PartiallyRefunded, "Partially Refunded", "warning"));

        yield return Of(
            Row<PaymentMethod>(1, PaymentMethodCodes.Cash, "Cash", "success"),
            Row<PaymentMethod>(2, PaymentMethodCodes.Card, "Card", "info"),
            Row<PaymentMethod>(3, PaymentMethodCodes.BankTransfer, "Bank Transfer", "primary"));

        yield return Of(
            Row<MovementType>(1, MovementTypeCodes.AddManually, "Add Manually", "success"),
            Row<MovementType>(2, MovementTypeCodes.Sale, "Sale", "info"),
            Row<MovementType>(3, MovementTypeCodes.ReturnFromClient, "Return from Client", "warning"),
            Row<MovementType>(4, MovementTypeCodes.RemoveManually, "Remove Manually", "error"),
            Row<MovementType>(5, MovementTypeCodes.TransferOut, "Transfer Out", "warning"),
            Row<MovementType>(6, MovementTypeCodes.TransferIn, "Transfer In", "info"),
            Row<MovementType>(7, MovementTypeCodes.TransferCancelled, "Transfer Cancelled", "neutral"));

        yield return Of(
            Row<StockTransferStatus>(1, StockTransferStatusCodes.Draft, "Draft", "neutral"),
            Row<StockTransferStatus>(2, StockTransferStatusCodes.Sent, "Sent", "info"),
            Row<StockTransferStatus>(3, StockTransferStatusCodes.PartiallyReceived, "Partially Received", "warning"),
            Row<StockTransferStatus>(4, StockTransferStatusCodes.Completed, "Completed", "success"),
            Row<StockTransferStatus>(5, StockTransferStatusCodes.Cancelled, "Cancelled", "error"));

        yield return Of(
            Row<SaleType>(1, SaleTypeCodes.Percentage, "Percentage", "info"),
            Row<SaleType>(2, SaleTypeCodes.FixedAmount, "Fixed Amount", "primary"));

        yield return Of(
            Row<DiscountType>(1, DiscountTypeCodes.None, "None", "neutral"),
            Row<DiscountType>(2, DiscountTypeCodes.Percent, "Percent", "info"),
            Row<DiscountType>(3, DiscountTypeCodes.Amount, "Amount", "primary"));

        yield return Of(
            Row<TenantStatus>(1, TenantStatusCodes.Active, "Active", "success"),
            Row<TenantStatus>(2, TenantStatusCodes.Suspended, "Suspended", "error"));

        yield return Of(
            Row<BusinessType>(1, BusinessTypeCodes.Retail, "Retail", "neutral"),
            Row<BusinessType>(2, BusinessTypeCodes.Cafe, "Cafe", "neutral"),
            Row<BusinessType>(3, BusinessTypeCodes.Restaurant, "Restaurant", "neutral"),
            Row<BusinessType>(4, BusinessTypeCodes.Pharmacy, "Pharmacy", "neutral"),
            Row<BusinessType>(5, BusinessTypeCodes.Grocery, "Grocery", "neutral"),
            Row<BusinessType>(6, BusinessTypeCodes.Other, "Other", "neutral"));

        yield return Of(
            Row<ExpenseLineType>(1, ExpenseLineTypeCodes.Amount, "Amount", "neutral"),
            Row<ExpenseLineType>(2, ExpenseLineTypeCodes.Fixed, "Fixed", "neutral"),
            Row<ExpenseLineType>(3, ExpenseLineTypeCodes.Percentage, "Percentage", "neutral"),
            Row<ExpenseLineType>(4, ExpenseLineTypeCodes.Quantity, "Quantity × Unit Cost", "neutral"));

        yield return Of(
            Row<ExpenseStatus>(1, ExpenseStatusCodes.Draft, "Draft", "neutral"),
            Row<ExpenseStatus>(2, ExpenseStatusCodes.PartiallyPaid, "Partially Paid", "warning"),
            Row<ExpenseStatus>(3, ExpenseStatusCodes.FullyPaid, "Fully Paid", "success"));
    }

    private static TLookup Row<TLookup>(int id, string code, string name, string tone)
        where TLookup : LookupEntity, new() =>
        new() { Id = id, Code = code, Name = name, Tone = tone, SortOrder = id };

    private static LookupEntity[] Of<TLookup>(params TLookup[] rows) where TLookup : LookupEntity => rows;
}
