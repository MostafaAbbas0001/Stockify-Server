namespace Stockify.Utils;

/// <summary>Prefixes of per-tenant document numbers (ORD-1, INV-1, ...).</summary>
public static class DocumentPrefixes
{
    public const string Order = "ORD";
    public const string Invoice = "INV";
    public const string CreditMemo = "CM";
    public const string Expense = "EXP";
}
