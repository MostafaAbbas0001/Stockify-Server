using System.ComponentModel.DataAnnotations;

namespace Stockify.Models.Dtos;

public class CreateTenantRequest
{
    [Required]
    [MaxLength(200)]
    public string CompanyName { get; set; } = null!;

    /// <summary>Optional URL-safe identifier; generated from the company name when empty.</summary>
    [MaxLength(100)]
    public string? Slug { get; set; }

    /// <summary>Code of a BusinessTypes row (RETAIL, CAFE, ...); case-insensitive.</summary>
    [Required]
    public string BusinessType { get; set; } = null!;

    [MaxLength(200)]
    public string FirstBranchName { get; set; } = "Main Branch";

    [Required]
    [MaxLength(200)]
    public string OwnerName { get; set; } = null!;

    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string OwnerEmail { get; set; } = null!;

    [Required]
    [MinLength(8)]
    public string OwnerPassword { get; set; } = null!;

    [Required]
    public CurrencyRequest BaseCurrency { get; set; } = new();

    /// <summary>Other currencies the company accepts, with their rate against the base currency.</summary>
    public List<CurrencyRequest> AdditionalCurrencies { get; set; } = new();

    [MaxLength(100)]
    public string TaxName { get; set; } = "VAT";

    /// <summary>Default tax rate as a fraction, e.g. 0.11 for 11%.</summary>
    [Range(0, 1)]
    public decimal TaxRate { get; set; }
}

public class CurrencyRequest
{
    [Required]
    [MaxLength(10)]
    public string Code { get; set; } = "USD";

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = "US Dollar";

    [MaxLength(10)]
    public string? Symbol { get; set; } = "$";

    /// <summary>Units of this currency per one unit of the base currency (1 for the base currency).</summary>
    [Range(typeof(decimal), "0.000001", "1000000000")]
    public decimal ExchangeRate { get; set; } = 1;
}

public class UpdateTenantStatusRequest
{
    [Required]
    public string Status { get; set; } = null!;
}
