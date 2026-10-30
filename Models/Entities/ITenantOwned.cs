namespace Stockify.Models.Entities;

/// <summary>
/// Marks an entity as belonging to one tenant (company). AppDbContext applies a
/// global query filter on TenantId and stamps it on insert, so services never set it.
/// </summary>
public interface ITenantOwned
{
    int TenantId { get; set; }
}
