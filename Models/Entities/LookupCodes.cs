namespace Stockify.Models.Entities;

// The stable codes of lookup-table rows that business rules depend on.
// Values, display names and badge colours live in the database (seeded by
// Infrastructure/Persistence/Seeds/LookupSeeds.cs); code only names the rows it acts on.

public static class OrderStatusCodes
{
    public const string Draft = "DRAFT";
    public const string InTransit = "IN_TRANSIT";
    public const string Delivered = "DELIVERED";
    public const string FullyPaid = "FULLY_PAID";
    public const string PartiallyPaid = "PARTIALLY_PAID";
    public const string Returned = "RETURNED";
    public const string PartiallyReturned = "PARTIALLY_RETURNED";
    public const string Cancelled = "CANCELLED";
}

public static class OrderTypeCodes
{
    public const string Delivery = "DELIVERY";
    public const string Pos = "POS";
}

public static class InvoiceStatusCodes
{
    public const string Issued = "ISSUED";
    public const string PartiallyPaid = "PARTIALLY_PAID";
    public const string FullyPaid = "FULLY_PAID";
    public const string Void = "VOID";
    public const string WrittenOff = "WRITTEN_OFF";
    public const string Refunded = "REFUNDED";
    public const string PartiallyRefunded = "PARTIALLY_REFUNDED";
}

public static class PaymentMethodCodes
{
    public const string Cash = "CASH";
    public const string Card = "CARD";
    public const string BankTransfer = "BANK_TRANSFER";
}

public static class MovementTypeCodes
{
    public const string AddManually = "ADD_MANUALLY";
    public const string Sale = "SALE";
    public const string ReturnFromClient = "RETURN_FROM_CLIENT";
    public const string RemoveManually = "REMOVE_MANUALLY";
    public const string TransferOut = "TRANSFER_OUT";
    public const string TransferIn = "TRANSFER_IN";
    public const string TransferCancelled = "TRANSFER_CANCELLED";
}

public static class StockTransferStatusCodes
{
    public const string Draft = "DRAFT";
    public const string Sent = "SENT";
    public const string PartiallyReceived = "PARTIALLY_RECEIVED";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";
}

public static class SaleTypeCodes
{
    public const string Percentage = "PERCENTAGE";
    public const string FixedAmount = "FIXED_AMOUNT";
}

public static class DiscountTypeCodes
{
    public const string None = "NONE";
    public const string Percent = "PERCENT";
    public const string Amount = "AMOUNT";
}

public static class TenantStatusCodes
{
    public const string Active = "ACTIVE";
    public const string Suspended = "SUSPENDED";
}

public static class BusinessTypeCodes
{
    public const string Retail = "RETAIL";
    public const string Cafe = "CAFE";
    public const string Restaurant = "RESTAURANT";
    public const string Pharmacy = "PHARMACY";
    public const string Grocery = "GROCERY";
    public const string Other = "OTHER";
}

public static class ExpenseLineTypeCodes
{
    /// <summary>Amount = the entered amount.</summary>
    public const string Amount = "AMOUNT";
    /// <summary>Amount = the entered rate.</summary>
    public const string Fixed = "FIXED";
    /// <summary>Amount = rate % of an employee's sales in the period.</summary>
    public const string Percentage = "PERCENTAGE";
    /// <summary>Amount = quantity × unit cost.</summary>
    public const string Quantity = "QUANTITY";
}

public static class ExpenseStatusCodes
{
    public const string Draft = "DRAFT";
    public const string PartiallyPaid = "PARTIALLY_PAID";
    public const string FullyPaid = "FULLY_PAID";
}

public static class RoleCodes
{
    public const string ExecutiveAdmin = "EXECUTIVE_ADMIN";
    public const string OrderManager = "ORDER_MANAGER";
    public const string StockManager = "STOCK_MANAGER";
    public const string PointOfSales = "POINT_OF_SALES";
}
