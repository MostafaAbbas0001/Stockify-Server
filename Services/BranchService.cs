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

        branch.Name = branch.Name.Trim();
        await EnsureUniqueNameAsync(branch.Name, null);
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

        var name = updated.Name.Trim();
        await EnsureUniqueNameAsync(name, id);
        branch.Name = name;
        branch.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return branch;
    }

    /// <summary>Active branch names are unique ignoring case (a deactivated branch's name can be reused).</summary>
    private async Task EnsureUniqueNameAsync(string name, int? exceptId)
    {
        var upper = name.ToUpper();
        if (await _db.Branches.AnyAsync(other =>
                !other.IsDeleted && other.Id != exceptId && other.Name.ToUpper() == upper))
            throw new InvalidOperationException("A branch with the same name already exists.");
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
        if (branch == null || branch.IsDeleted)
            throw new KeyNotFoundException("Branch not found.");

        // Only an empty, unstaffed branch with nothing in transit can be closed (D24).
        if (!await _db.Branches.AnyAsync(other => other.Id != id && !other.IsDeleted))
            throw new InvalidOperationException("The company's last branch cannot be deleted.");
        if (await _db.BranchLotStocks.AnyAsync(stock => stock.BranchId == id && stock.Quantity > 0))
            throw new InvalidOperationException("This branch still has stock. Transfer or remove it first.");
        if (await _db.Users.AnyAsync(user => user.BranchId == id))
            throw new InvalidOperationException("Users are assigned to this branch. Move them to another branch first.");
        if (await _db.Employees.AnyAsync(employee => employee.BranchId == id))
            throw new InvalidOperationException("Employees are assigned to this branch. Move them to another branch first.");
        var inTransit = new[] { StockTransferStatusCodes.Sent, StockTransferStatusCodes.PartiallyReceived };
        if (await _db.StockTransfers.AnyAsync(transfer =>
                (transfer.SourceBranchId == id || transfer.DestinationBranchId == id) && inTransit.Contains(transfer.Status.Code)))
            throw new InvalidOperationException("Stock is in transit to or from this branch. Receive or cancel the transfers first.");

        branch.IsDeleted = true;
        branch.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return true;
    }
}
