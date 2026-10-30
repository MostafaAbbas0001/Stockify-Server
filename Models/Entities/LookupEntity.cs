namespace Stockify.Models.Entities;

/// <summary>
/// A fixed list stored as a database table (statuses, types, methods) instead of an enum.
/// Rows are seeded and global (not tenant-owned). Business rules refer to a row by its
/// stable <see cref="Code"/> (see Lookups/LookupCodes.cs), never by its numeric id.
/// </summary>
public abstract class LookupEntity
{
    public int Id { get; set; }

    /// <summary>Stable identifier used by code and by client translations, e.g. "DRAFT".</summary>
    public string Code { get; set; } = null!;

    /// <summary>Default (English) display name.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Badge colour for the UI: neutral, info, success, warning, error or primary.</summary>
    public string Tone { get; set; } = "neutral";

    public int SortOrder { get; set; }
}
