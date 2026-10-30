namespace Stockify.Services;

public class DeliveryLocationService
{
    private readonly AppDbContext _db;

    public DeliveryLocationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<DeliveryLocation> CreateAsync(DeliveryLocation body)
    {
        if (body is null)
            throw new ArgumentException("Body is required.");

        var name = body.LocationName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("LocationName is required.");

        if (body.Fee < 0)
            throw new ArgumentException("Fee must be a non-negative number.");

        var exists = await _db.DeliveryLocations
            .AnyAsync(ld => ld.LocationName.ToLower() == name.ToLower());

        if (exists)
            throw new InvalidOperationException("A delivery charge for this location already exists.");

        var entity = new DeliveryLocation
        {
            LocationName = name,
            Fee = body.Fee,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = null
        };

        await _db.DeliveryLocations.AddAsync(entity);
        await _db.SaveChangesAsync();

        return entity;
    }

    public async Task<IEnumerable<DeliveryLocation>> GetAllAsync()
    {
        return await _db.DeliveryLocations.ToListAsync();
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _db.DeliveryLocations.FindAsync(id);
        if (entity == null)
            throw new KeyNotFoundException("Location delivery charge not found.");

        var inUse = await _db.Orders.AnyAsync(o => o.DeliveryLocationId == id);
        if (inUse)
            throw new InvalidOperationException("Cannot delete: this delivery charge is used by existing orders.");

        if (entity.IsInStore)
            throw new ArgumentException("Cannot delete the built-in in-store location.");

        _db.DeliveryLocations.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<DeliveryLocation> UpdateAsync(int id, DeliveryLocation body)
    {
        var entity = await _db.DeliveryLocations.FindAsync(id);
        if (entity == null)
            throw new KeyNotFoundException("Location delivery charge not found.");

        if (body == null)
            throw new ArgumentException("Body is required.");

        if (body.Fee < 0)
            throw new ArgumentException("Fee must be a non-negative number.");

        entity.LocationName = body.LocationName;
        entity.Fee = body.Fee;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return entity;
    }
}
