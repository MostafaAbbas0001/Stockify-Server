namespace Stockify.Models.Dtos;

/// <summary>Variants to put on a promotion (a variant has at most one promotion; this moves it).</summary>
public class AssignSaleVariantsRequest
{
    public List<int> VariantIds { get; set; } = new();
}
