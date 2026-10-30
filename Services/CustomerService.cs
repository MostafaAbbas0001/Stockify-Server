namespace Stockify.Services;

public class CustomerService
{
    private const int PageSize = 20;
    private readonly AppDbContext _db;

    public CustomerService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<object> GetAllAsync(string? search, int page)
    {
        page = Math.Max(page, 1);
        var query = _db.Customers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().ToLower()}%";
            query = query.Where(customer =>
                EF.Functions.Like(customer.Name.ToLower(), pattern) ||
                EF.Functions.Like(customer.Phone.ToLower(), pattern) ||
                EF.Functions.Like(customer.Email.ToLower(), pattern) ||
                EF.Functions.Like(customer.Address.ToLower(), pattern));
        }

        var totalCount = await query.CountAsync();
        var customers = await query
            .OrderByDescending(customer => customer.CreatedAt)
            .ThenByDescending(customer => customer.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        return new
        {
            totalCount,
            page,
            perPage = PageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)PageSize),
            customers
        };
    }

    public async Task<Customer> GetByIdAsync(int id)
    {
        if (id <= 0)
            throw new ArgumentException("Invalid customer ID.");

        var customer = await _db.Customers.FindAsync(id);
        if (customer == null)
            throw new KeyNotFoundException("Customer not found.");

        return customer;
    }

    public async Task<Customer> CreateAsync(CreateCustomerRequest customerDto)
    {
        if (customerDto == null || string.IsNullOrWhiteSpace(customerDto.Name))
            throw new ArgumentException("Customer name is required.");

        var customer = new Customer
        {
            Name = customerDto.Name,
            Phone = customerDto.Phone,
            Address = customerDto.Address,
            Email = customerDto.Email
        };

        await _db.Customers.AddAsync(customer);
        await _db.SaveChangesAsync();

        return customer;
    }

    public async Task<Customer> UpdateAsync(int id, UpdateCustomerRequest customerDto)
    {
        if (id <= 0)
            throw new ArgumentException("Invalid customer ID.");

        if (customerDto == null || string.IsNullOrWhiteSpace(customerDto.Name))
            throw new ArgumentException("Customer name is required.");

        var customer = await _db.Customers.FindAsync(id);
        if (customer == null)
            throw new KeyNotFoundException("Customer not found.");

        customer.Name = customerDto.Name;
        customer.Phone = customerDto.Phone;
        customer.Address = customerDto.Address;
        customer.Email = customerDto.Email;

        await _db.SaveChangesAsync();

        return customer;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        if (id <= 0)
            throw new ArgumentException("Invalid customer ID.");

        var customer = await _db.Customers.FindAsync(id);
        if (customer == null)
            throw new KeyNotFoundException("Customer not found.");

        if (await _db.Orders.AnyAsync(order => order.CustomerId == id))
            throw new ArgumentException("Customer has existing orders.");

        _db.Customers.Remove(customer);
        await _db.SaveChangesAsync();

        return true;
    }
}
