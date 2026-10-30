namespace Stockify.Services;

public class CategoryService
{
    private readonly AppDbContext _db;

    public CategoryService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Category> CreateAsync(Category category)
    {
        if (category == null || string.IsNullOrWhiteSpace(category.Name))
            throw new ArgumentException("Category data are invalid.");

        var normalizedName = NormalizeLabel(category.Name);
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new ArgumentException("Category name is invalid.");

        var normalizedUpper = normalizedName.ToUpper();
        var exists = await _db.Categories.AnyAsync(c => c.Name.ToUpper() == normalizedUpper);
        if (exists)
            throw new InvalidOperationException("A category with the same name already exists.");

        var newCategory = new Category
        {
            Name = normalizedName
        };

        await _db.Categories.AddAsync(newCategory);
        await _db.SaveChangesAsync();

        return newCategory;
    }

    public async Task<Category> UpdateAsync(int id, Category updatedCategory)
    {
        if (id <= 0)
            throw new ArgumentException("Category data are invalid.");

        if (updatedCategory == null || string.IsNullOrWhiteSpace(updatedCategory.Name))
            throw new ArgumentException("Category data are invalid.");

        var existingCategory = await _db.Categories.FindAsync(id);
        if (existingCategory == null)
            throw new KeyNotFoundException("Category not found.");

        var normalizedName = NormalizeLabel(updatedCategory.Name);
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new ArgumentException("Category name is invalid.");

        var normalizedUpper = normalizedName.ToUpper();
        var exists = await _db.Categories
            .Where(c => c.Id != id)
            .AnyAsync(c => c.Name.ToUpper() == normalizedUpper);
        if (exists)
            throw new InvalidOperationException("A category with the same name already exists.");

        existingCategory.Name = normalizedName;
        await _db.SaveChangesAsync();

        return existingCategory;
    }

    public async Task<IEnumerable<object>> GetAllAsync()
    {
        return await _db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync();
    }

    public async Task<bool> DeleteAsync(int id)
    {
        if (id <= 0)
            throw new ArgumentException("Invalid category ID.");

        var category = await _db.Categories.FindAsync(id);
        if (category == null)
            throw new KeyNotFoundException("Category not found.");

        var hasProducts = await _db.Products.AnyAsync(product => product.CategoryId == id);
        if (hasProducts)
            throw new InvalidOperationException("Cannot delete a category that has products.");

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();
        return true;
    }

    private static string NormalizeLabel(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var trimmed = name.Trim();
        trimmed = System.Text.RegularExpressions.Regex.Replace(trimmed, @"\s+", " ");
        return trimmed;
    }
}
