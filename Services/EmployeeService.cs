namespace Stockify.Services;

public class EmployeeService
{
    private const int PageSize = 20;
    private readonly AppDbContext _db;

    public EmployeeService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Employee> CreateAsync(CreateEmployeeRequest dto)
    {
        if (dto is null)
            throw new ArgumentException("Body is required.");

        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Name is required.");

        var branchExists = await _db.Branches.AnyAsync(b => b.Id == dto.BranchId);
        if (!branchExists)
            throw new ArgumentException($"BranchId '{dto.BranchId}' does not exist.");

        var emailTaken = await _db.Employees.AnyAsync(e => e.Email == dto.Email);
        if (emailTaken)
            throw new InvalidOperationException("An employee with this email already exists.");

        var employee = new Employee
        {
            Name = dto.Name.Trim(),
            Phone = dto.Phone?.Trim() ?? "",
            Address = dto.Address?.Trim() ?? "",
            Email = dto.Email?.Trim() ?? string.Empty,
            BranchId = dto.BranchId,
            IsSale = dto.IsSale
        };

        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();

        return employee;
    }

    public async Task<object> GetByIdAsync(int id)
    {
        var employee = await _db.Employees
            .Include(e => e.Branch)
            .Where(e => e.Id == id)
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.Phone,
                e.Address,
                e.Email,
                e.IsSale,
                e.BranchId,
                BranchName = e.Branch.Name
            })
            .FirstOrDefaultAsync();

        if (employee == null)
            throw new KeyNotFoundException($"Employee with ID {id} not found.");

        return employee;
    }

    public async Task<object> GetAllAsync(
        string? search,
        int? branchId,
        bool? isSale,
        int page)
    {
        page = Math.Max(page, 1);
        // The controller has already limited branchId to what the caller may see.
        int? effectiveBranchId = branchId;

        var employeesQuery = _db.Employees
            .Include(e => e.Branch)
            .AsQueryable();

        if (effectiveBranchId.HasValue)
            employeesQuery = employeesQuery.Where(e => e.BranchId == effectiveBranchId.Value);
        if (isSale.HasValue)
            employeesQuery = employeesQuery.Where(employee => employee.IsSale == isSale.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().ToLower()}%";
            employeesQuery = employeesQuery.Where(employee =>
                EF.Functions.Like(employee.Name.ToLower(), pattern) ||
                (employee.Email != null && EF.Functions.Like(employee.Email.ToLower(), pattern)) ||
                (employee.Phone != null && EF.Functions.Like(employee.Phone.ToLower(), pattern)) ||
                EF.Functions.Like(employee.Branch.Name.ToLower(), pattern));
        }

        var totalCount = await employeesQuery.CountAsync();
        var employees = await employeesQuery
            .OrderByDescending(employee => employee.CreatedAt)
            .ThenByDescending(employee => employee.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.Email,
                e.Phone,
                e.Address,
                e.IsSale,
                BranchId = e.BranchId,
                BranchName = e.Branch.Name,
                e.CreatedAt
            })
            .ToListAsync();

        return new
        {
            totalCount,
            page,
            perPage = PageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)PageSize),
            employees
        };
    }

    public async Task<IEnumerable<object>> GetMyBranchEmployeesAsync(int branchId)
    {
        if (branchId <= 0)
            throw new UnauthorizedAccessException("Branch ID not found in token.");

        var employees = await _db.Employees
            .Where(e => e.BranchId == branchId && e.IsSale)
            .OrderBy(e => e.Name)
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.Phone,
                e.Address,
                e.Email,
                e.IsSale,
                e.BranchId
            })
            .ToListAsync();

        return employees;
    }

    public async Task<Employee> UpdateAsync(int id, UpdateEmployeeRequest dto)
    {
        if (dto is null)
            throw new ArgumentException("Body is required.");

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (employee is null)
            throw new KeyNotFoundException("Employee not found.");

        if (!string.IsNullOrWhiteSpace(dto.Name))
            employee.Name = dto.Name.Trim();

        if (dto.Phone != null)
            employee.Phone = dto.Phone.Trim();

        if (dto.Address != null)
            employee.Address = dto.Address.Trim();

        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            var emailTaken = await _db.Employees
                .AnyAsync(e => e.Email == dto.Email && e.Id != id);

            if (emailTaken)
                throw new InvalidOperationException("An employee with this email already exists.");

            employee.Email = dto.Email.Trim();
        }

        if (dto.BranchId.HasValue)
        {
            var branchExists = await _db.Branches.AnyAsync(b => b.Id == dto.BranchId.Value);
            if (!branchExists)
                throw new ArgumentException($"BranchId '{dto.BranchId.Value}' does not exist.");

            employee.BranchId = dto.BranchId.Value;
        }

        if (dto.IsSale.HasValue)
            employee.IsSale = dto.IsSale.Value;

        await _db.SaveChangesAsync();
        return employee;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (employee is null)
            throw new KeyNotFoundException("Employee not found.");

        var hasOrders = await _db.Orders.AnyAsync(o => o.CreatedByEmployeeId == id);
        if (hasOrders)
            throw new InvalidOperationException("Cannot delete employee because they are associated with existing orders.");

        _db.Employees.Remove(employee);
        await _db.SaveChangesAsync();

        return true;
    }

    public async Task<decimal> GetOrderAmountsPerEmployee(EmployeeSalesRequest request)
    {
        return await CalculateEmployeeSales(request.EmployeeId, request.StartDate, request.EndDate);
    }

    private async Task<decimal> CalculateEmployeeSales(int employeeId, DateTime startDate, DateTime endDate)
    {
        var orders = await _db.Orders
            .Include(o => o.OrderStatus)
            .Include(o => o.Invoices)
            .Where(o => o.CreatedByEmployeeId == employeeId &&
                   o.CreatedAt >= startDate &&
                   o.CreatedAt <= endDate &&
                   o.OrderStatus.Code == OrderStatusCodes.FullyPaid)
            .ToListAsync();

        decimal totalSales = orders.SelectMany(o => o.Invoices).Sum(i => i.TotalAmount);

        return totalSales;
    }
}
