namespace Stockify.Services;

public class BranchService
{
    private readonly AppDbContext _db;

    public BranchService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> CreateAsync(Branch branch)
    {
        if (branch == null || string.IsNullOrWhiteSpace(branch.Name))
            throw new ArgumentException("Branch data is invalid.");

        branch.CreatedAt = DateTime.UtcNow;
        branch.UpdatedAt = DateTime.UtcNow;
        branch.IsDeleted = false;

        using var tx = await _db.Database.BeginTransactionAsync();

        await _db.Branches.AddAsync(branch);
        await _db.SaveChangesAsync();

        var allVariantIds = await _db.ProductVariants
            .Select(v => v.Id)
            .ToListAsync();

        foreach (var variantId in allVariantIds)
        {
            _db.LocationStocks.Add(new LocationStock
            {
                BranchId = branch.Id,
                ProductVariantId = variantId,
                ReorderLevel = 0,
                LastUpdated = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return true;
    }

    public async Task<Branch> UpdateAsync(int id, Branch updated)
    {
        var branch = await _db.Branches.FindAsync(id);
        if (branch == null)
            throw new KeyNotFoundException("Branch not found.");

        if (string.IsNullOrWhiteSpace(updated.Name))
            throw new ArgumentException("Branch name is invalid.");

        branch.Name = updated.Name;
        branch.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return branch;
    }

    public async Task<IEnumerable<Branch>> GetAllAsync()
    {
        return await _db.Branches
            .AsNoTracking()
            .Where(b => !b.IsDeleted)
            .ToListAsync();
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var branch = await _db.Branches.FindAsync(id);
        if (branch == null)
            throw new KeyNotFoundException("Branch not found.");

        branch.IsDeleted = true;
        branch.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return true;
    }
}
