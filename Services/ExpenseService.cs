namespace Stockify.Services;

public class ExpenseService
{
    private const int PageSize = 20;
    private readonly AppDbContext _db;
    private readonly EmployeeService _employeeService;
    private readonly DocumentNumberService _documentNumbers;
    private readonly LookupService _lookups;

    public ExpenseService(
        AppDbContext db,
        EmployeeService employeeService,
        DocumentNumberService documentNumbers,
        LookupService lookups)
    {
        _db = db;
        _employeeService = employeeService;
        _documentNumbers = documentNumbers;
        _lookups = lookups;
    }

    public async Task<object> CreateExpenseAsync(CreateExpenseRequest dto, int userId)
    {
        if (dto.Lines == null || dto.Lines.Count == 0)
            throw new ArgumentException("At least one expense line is required.");

        var mainCategoryExists = await _db.ExpenseCategories
            .Where(c => c.Id == dto.ExpenseCategoryId)
            .AnyAsync();

        if (!mainCategoryExists)
            throw new ArgumentException($"Invalid expense category ID: {dto.ExpenseCategoryId}");

        await EnsureLineReferencesExistAsync(dto.Lines);

        await using var transaction = await _db.Database.BeginTransactionAsync();

        var expense = new Expense
        {
            ExpenseNumber = await _documentNumbers.NextAsync(DocumentPrefixes.Expense),
            ExpenseCategoryId = dto.ExpenseCategoryId,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Expenses.Add(expense);
        await _db.SaveChangesAsync();

        var categoryIds = dto.Lines.Select(l => l.CategoryId).ToList();
        var categories = await _db.ExpenseCategories
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        var expenseLines = new List<ExpenseLine>();
        var draftStatusId = await _lookups.IdAsync<ExpenseStatus>(ExpenseStatusCodes.Draft);

        foreach (var lineDto in dto.Lines)
        {
            if (!categories.TryGetValue(lineDto.CategoryId, out _))
            {
                await transaction.RollbackAsync();
                throw new ArgumentException($"Invalid CategoryId {lineDto.CategoryId}.");
            }

            var lineType = lineDto.LineTypeId is int lineTypeId
                ? await _lookups.FindAsync<ExpenseLineType>(lineTypeId)
                    ?? throw new ArgumentException($"Expense line type {lineTypeId} does not exist.")
                : null;

            decimal amount = lineDto.Amount;
            if (lineType?.Code == ExpenseLineTypeCodes.Percentage &&
                lineDto.EmployeeId.HasValue && lineDto.Rate.HasValue &&
                lineDto.StartDate.HasValue && lineDto.EndDate.HasValue)
            {
                var salesRequest = new EmployeeSalesRequest
                {
                    EmployeeId = lineDto.EmployeeId.Value,
                    StartDate = lineDto.StartDate.Value,
                    EndDate = lineDto.EndDate.Value
                };
                decimal totalSales = await _employeeService.GetOrderAmountsPerEmployee(salesRequest);
                amount = totalSales * (lineDto.Rate.Value / 100);
            }
            else if (lineType?.Code == ExpenseLineTypeCodes.Fixed)
            {
                amount = lineDto.Rate ?? 0;
            }
            else if (lineType?.Code == ExpenseLineTypeCodes.Quantity &&
                     lineDto.Quantity.HasValue && lineDto.UnitCost.HasValue)
            {
                amount = lineDto.Quantity.Value * lineDto.UnitCost.Value;
            }

            if (amount <= 0)
            {
                await transaction.RollbackAsync();
                throw new ArgumentException("Each expense line must have a total greater than zero.");
            }

            var expenseLine = new ExpenseLine
            {
                Expense = expense,
                CategoryId = lineDto.CategoryId,
                EmployeeId = lineDto.EmployeeId,
                TotalAmount = amount,
                CurrencyId = lineDto.CurrencyId,
                StartDate = lineDto.StartDate,
                EndDate = lineDto.EndDate,
                ItemName = lineDto.ItemName,
                LineTypeId = lineType?.Id,
                StatusId = draftStatusId,
                Rate = lineDto.Rate,
                Note = lineDto.Note,
                PurchaseDate = lineDto.PurchaseDate,
                Quantity = lineDto.Quantity,
                UnitCost = lineDto.UnitCost,
                SupplierName = lineDto.SupplierName,
                BranchId = lineDto.BranchId,
                AdditionalInfo = lineDto.AdditionalInfo,
                ReferenceNumber = lineDto.ReferenceNumber,
                ExpensePeriodStart = lineDto.ExpensePeriodStart,
                ExpensePeriodEnd = lineDto.ExpensePeriodEnd
            };
            expenseLines.Add(expenseLine);
        }

        _db.ExpenseLines.AddRange(expenseLines);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return new
        {
            id = expense.Id,
            lineCount = dto.Lines.Count
        };
    }

    /// <summary>
    /// Rejects employee, branch and currency ids that do not exist in the current tenant
    /// (the tenant query filter hides other tenants' rows).
    /// </summary>
    private async Task EnsureLineReferencesExistAsync(IReadOnlyCollection<CreateExpenseLineRequest> lines)
    {
        var employeeIds = lines.Where(l => l.EmployeeId.HasValue).Select(l => l.EmployeeId!.Value).Distinct().ToList();
        var branchIds = lines.Where(l => l.BranchId.HasValue).Select(l => l.BranchId!.Value).Distinct().ToList();
        var currencyIds = lines.Where(l => l.CurrencyId.HasValue).Select(l => l.CurrencyId!.Value).Distinct().ToList();

        if (employeeIds.Count > 0 &&
            await _db.Employees.CountAsync(e => employeeIds.Contains(e.Id)) != employeeIds.Count)
            throw new ArgumentException("One or more employees were not found.");
        if (branchIds.Count > 0 &&
            await _db.Branches.CountAsync(b => branchIds.Contains(b.Id)) != branchIds.Count)
            throw new ArgumentException("One or more branches were not found.");
        if (currencyIds.Count > 0 &&
            await _db.Currencies.CountAsync(c => currencyIds.Contains(c.Id)) != currencyIds.Count)
            throw new ArgumentException("One or more currencies were not found.");
    }

    public async Task<object> GetExpenseByIdAsync(int id)
    {
        var expense = await _db.Expenses
            .AsNoTracking()
            .Include(e => e.ExpenseCategory)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (expense == null)
            throw new KeyNotFoundException($"Expense with ID {id} not found.");

        var createdByUsername = await _db.Users
            .Where(u => u.Id == expense.CreatedByUserId)
            .Select(u => u.Username)
            .FirstOrDefaultAsync();

        var lines = await _db.ExpenseLines
            .Where(l => l.ExpenseId == id)
            .Include(l => l.Category)
            .Include(l => l.Status)
            .Include(l => l.Employee)
            .Include(l => l.Currency)
            .Include(l => l.Branch)
            .Include(l => l.Payments)
            .AsNoTracking()
            .Select(l => new
            {
                l.Id,
                l.CategoryId,
                CategoryName = l.Category != null ? l.Category.Name : null,
                CategoryCode = l.Category != null ? l.Category.Code : null,
                l.TotalAmount,
                l.CurrencyId,
                CurrencyCode = l.Currency != null ? l.Currency.Code : null,
                CurrencySymbol = l.Currency != null ? l.Currency.Symbol : null,
                l.Note,
                l.StatusId,
                StatusCode = l.Status.Code,
                StatusName = l.Status.Name,
                l.EmployeeId,
                EmployeeName = l.Employee != null ? l.Employee.Name : null,
                l.StartDate,
                l.EndDate,
                l.ItemName,
                l.PurchaseDate,
                l.Quantity,
                l.UnitCost,
                l.SupplierName,
                l.BranchId,
                BranchName = l.Branch != null ? l.Branch.Name : null,
                l.LineTypeId,
                LineTypeCode = l.LineType != null ? l.LineType.Code : null,
                Payments = l.Payments.Select(p => new
                {
                    p.Id,
                    p.ExpenseLineId,
                    p.Amount,
                    p.PaymentDate,
                    p.PaymentMethodId,
                    PaymentMethodCode = p.PaymentMethod.Code,
                    PaymentMethodName = p.PaymentMethod.Name,
                    p.PaymentReference,
                    p.Notes,
                    p.CreatedAt,
                    p.CreatedByUserId,
                    p.ApprovedAt,
                    p.ApprovedByUserId,
                    p.RejectionReason
                }).ToList(),
                l.ExpensePeriodStart,
                l.ExpensePeriodEnd,
                l.ReferenceNumber,
                l.AdditionalInfo
            })
            .ToListAsync();

        var result = new
        {
            expense.Id,
            CategoryName = expense.ExpenseCategory?.Name,
            CategoryCode = expense.ExpenseCategory?.Code,
            expense.ExpenseNumber,
            expense.ExpenseCategoryId,
            expense.CreatedAt,
            expense.UpdatedAt,
            expense.CreatedByUserId,
            CreatedByUsername = createdByUsername,
            Lines = lines
        };

        return result;
    }

    public async Task<object> ListExpensesAsync(
        string? search,
        DateTime? from,
        DateTime? to,
        int? categoryId,
        int? branchId,
        int? statusId,
        int page)
    {
        page = Math.Max(page, 1);
        var query = _db.Expenses
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().ToLower()}%";
            query = query.Where(expense =>
                (expense.ExpenseNumber != null &&
                 EF.Functions.Like(expense.ExpenseNumber.ToLower(), pattern)) ||
                EF.Functions.Like(expense.ExpenseCategory.Name.ToLower(), pattern) ||
                (expense.CreatedBy != null &&
                 EF.Functions.Like(expense.CreatedBy.Username.ToLower(), pattern)) ||
                _db.ExpenseLines.Any(line =>
                    line.ExpenseId == expense.Id &&
                    ((line.ItemName != null && EF.Functions.Like(line.ItemName.ToLower(), pattern)) ||
                     (line.SupplierName != null && EF.Functions.Like(line.SupplierName.ToLower(), pattern)) ||
                     (line.ReferenceNumber != null && EF.Functions.Like(line.ReferenceNumber.ToLower(), pattern)) ||
                     (line.Note != null && EF.Functions.Like(line.Note.ToLower(), pattern)))));
        }

        // Filter by expense line period overlap when a date range is provided
        if (from.HasValue || to.HasValue)
        {
            var fromDate = from ?? DateTime.MinValue;
            var toDate = to ?? DateTime.MaxValue;

            var expenseIds = await _db.ExpenseLines
                .Where(l =>
                    // Only use expense period dates - no fallbacks
                    (l.ExpensePeriodStart.HasValue && l.ExpensePeriodEnd.HasValue) && 
                    (l.ExpensePeriodStart.Value <= toDate && l.ExpensePeriodEnd.Value >= fromDate))
                .Select(l => l.ExpenseId)
                .Distinct()
                .ToListAsync();

            query = query.Where(e => expenseIds.Contains(e.Id));
        }

        if (categoryId.HasValue)
        {
            var expenseIds = await _db.ExpenseLines
                .Where(l => l.CategoryId == categoryId.Value)
                .Select(l => l.ExpenseId)
                .Distinct()
                .ToListAsync();

            query = query.Where(e => expenseIds.Contains(e.Id));
        }

        if (branchId.HasValue)
            query = query.Where(expense => _db.ExpenseLines.Any(line =>
                line.ExpenseId == expense.Id && line.BranchId == branchId.Value));

        if (statusId.HasValue)
            query = query.Where(expense => _db.ExpenseLines.Any(line =>
                line.ExpenseId == expense.Id && line.StatusId == statusId.Value));

        var totalCount = await query.CountAsync();

        var expenses = await query
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        var categoryIds = expenses.Select(e => e.ExpenseCategoryId).Distinct().ToList();
        var categories = await _db.ExpenseCategories
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        var selectedExpenseIds = expenses.Select(e => e.Id).ToList();
        var expenseLines = await _db.ExpenseLines
            .Include(l => l.Status)
            .Include(l => l.Currency)
            .Where(l => selectedExpenseIds.Contains(l.ExpenseId))
            .ToListAsync();

        var expenseLineIds = expenseLines.Select(l => l.Id).ToList();
        var approvedPaymentsByExpenseId = await _db.ExpenseLinePayments
            .Where(p => expenseLineIds.Contains(p.ExpenseLineId) && p.ApprovedAt != null && p.RejectionReason == null)
            .Include(p => p.ExpenseLine)
            .GroupBy(p => p.ExpenseLine.ExpenseId)
            .ToDictionaryAsync(g => g.Key, g => g.Sum(p => p.Amount));

        var expenseLineSummary = expenseLines
            .GroupBy(l => l.ExpenseId)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    TotalAmount = g.Sum(l => l.TotalAmount),
                    PaymentStatusCode =
                        g.All(l => l.Status.Code == ExpenseStatusCodes.FullyPaid) ? ExpenseStatusCodes.FullyPaid :
                        g.Any(l => l.Status.Code != ExpenseStatusCodes.Draft) ? ExpenseStatusCodes.PartiallyPaid :
                        ExpenseStatusCodes.Draft
                });

        var expensePeriodSummary = expenseLines
            .GroupBy(l => l.ExpenseId)
            .ToDictionary(
                g => g.Key,
                g => {
                    var validStartDates = g
                        .Where(l => l.ExpensePeriodStart.HasValue)
                        .Select(l => l.ExpensePeriodStart.GetValueOrDefault());
                    var validEndDates = g
                        .Where(l => l.ExpensePeriodEnd.HasValue)
                        .Select(l => l.ExpensePeriodEnd.GetValueOrDefault());
                    
                    return new {
                        // Only use expense period dates - no fallbacks
                        MinPeriodStart = validStartDates.Any() ? validStartDates.Min() : (DateTime?)null,
                        MaxPeriodEnd = validEndDates.Any() ? validEndDates.Max() : (DateTime?)null
                    };
                });

        var expenseCurrencySummary = expenseLines
            .GroupBy(l => l.ExpenseId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .Where(l => l.CurrencyId.HasValue)
                    .GroupBy(l => new { l.CurrencyId, Code = l.Currency!.Code })
                    .Select(x => new
                    {
                        CurrencyId = x.Key.CurrencyId,
                        CurrencyCode = x.Key.Code,
                        CurrencySymbol = x.First().Currency!.Symbol,
                        TotalAmount = x.Sum(l => l.TotalAmount)
                    })
                    .ToList());

        var response = expenses.Select(e => new
        {
            Id = e.Id,
            ExpenseNumber = e.ExpenseNumber,
            ExpenseCategoryId = e.ExpenseCategoryId,
            CategoryName = categories.ContainsKey(e.ExpenseCategoryId) ? categories[e.ExpenseCategoryId] : null,
            TotalAmount = expenseLineSummary.ContainsKey(e.Id) ? expenseLineSummary[e.Id].TotalAmount : 0,
            PaidAmount = approvedPaymentsByExpenseId.ContainsKey(e.Id) ? approvedPaymentsByExpenseId[e.Id] : 0,
            PaymentStatusCode = expenseLineSummary.ContainsKey(e.Id)
                ? expenseLineSummary[e.Id].PaymentStatusCode
                : ExpenseStatusCodes.Draft,
            CreatedAt = e.CreatedAt,
            ExpensePeriodStart = expensePeriodSummary.ContainsKey(e.Id) ? expensePeriodSummary[e.Id].MinPeriodStart : null,
            ExpensePeriodEnd = expensePeriodSummary.ContainsKey(e.Id) ? expensePeriodSummary[e.Id].MaxPeriodEnd : null,
            Currencies = expenseCurrencySummary.ContainsKey(e.Id)
                ? expenseCurrencySummary[e.Id].Cast<object>().ToList()
                : new List<object>()
        }).ToList();

        return new
        {
            totalCount,
            page,
            perPage = PageSize,
            totalPages = (int)Math.Ceiling((double)totalCount / PageSize),
            expenses = response
        };
    }

    public async Task<object> ListExpenseCategoriesAsync(int? parentId)
    {
        var query = _db.ExpenseCategories.AsNoTracking();

        if (parentId.HasValue)
        {
            query = query.Where(c => c.ParentId == parentId.Value);
        }
        else
        {
            query = query.Where(c => c.ParentId == null);
        }

        var categories = await query.OrderBy(c => c.Name).ToListAsync();

        var response = categories.Select(c => new
        {
            id = c.Id,
            name = c.Name,
            code = c.Code
        });

        return response;
    }

    public async Task<bool> UpdateExpenseLineStatusAsync(ExpenseLine expenseLine)
    {
        decimal totalPaid = expenseLine.Payments
            .Where(p => p.ApprovedAt != null && p.RejectionReason == null)
            .Sum(p => p.Amount);

        var statusCode = totalPaid >= expenseLine.TotalAmount ? ExpenseStatusCodes.FullyPaid
            : totalPaid > 0 ? ExpenseStatusCodes.PartiallyPaid
            : ExpenseStatusCodes.Draft;
        expenseLine.StatusId = await _lookups.IdAsync<ExpenseStatus>(statusCode);

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<object> CreatePaymentAsync(
        CreateExpenseLinePaymentRequest dto,
        int userId)
    {
        var expenseLine = await _db.ExpenseLines
            .Include(l => l.Payments)
            .FirstOrDefaultAsync(l => l.Id == dto.ExpenseLineId);

        if (expenseLine == null)
            throw new KeyNotFoundException($"Expense line with ID {dto.ExpenseLineId} not found.");

        if (await _lookups.FindAsync<PaymentMethod>(dto.PaymentMethodId) == null)
            throw new ArgumentException($"Payment method {dto.PaymentMethodId} does not exist.");

        var payment = new ExpenseLinePayment
        {
            ExpenseLineId = dto.ExpenseLineId,
            Amount = dto.Amount,
            PaymentDate = dto.PaymentDate.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dto.PaymentDate, DateTimeKind.Utc)
                : dto.PaymentDate.Kind == DateTimeKind.Local
                    ? dto.PaymentDate.ToUniversalTime()
                    : dto.PaymentDate,
            PaymentMethodId = dto.PaymentMethodId,
            PaymentReference = dto.PaymentReference,
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId
        };

        _db.ExpenseLinePayments.Add(payment);
        await _db.SaveChangesAsync();

        await UpdateExpenseLineStatusAsync(expenseLine);

        return new
        {
            id = payment.Id
        };
    }

    public async Task<object> MakePaymentDecisionAsync(
        int id,
        ExpensePaymentDecisionRequest dto,
        int userId)
    {
        var payment = await _db.ExpenseLinePayments
            .Include(p => p.ExpenseLine)
                .ThenInclude(el => el.Payments)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (payment == null)
            throw new KeyNotFoundException($"Payment with ID {id} not found.");

        if (dto.IsApproved)
        {
            var approvedPaid = payment.ExpenseLine.Payments
                .Where(p => p.ApprovedAt != null && p.RejectionReason == null && p.Id != payment.Id)
                .Sum(p => p.Amount);

            var remaining = payment.ExpenseLine.TotalAmount - approvedPaid;
            const decimal tolerance = 0.01m;

            if (payment.Amount - remaining > tolerance)
                throw new ArgumentException($"Payment amount exceeds remaining balance. Remaining: {remaining}");

            payment.ApprovedAt = DateTime.UtcNow;
            payment.ApprovedByUserId = userId;
            payment.RejectionReason = null;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(dto.RejectionReason))
                throw new ArgumentException("Rejection reason is required.");

            payment.RejectionReason = dto.RejectionReason;
            payment.ApprovedAt = DateTime.UtcNow; // Using ApprovedAt for decision date
            payment.ApprovedByUserId = userId;
        }

        await _db.SaveChangesAsync();
        await UpdateExpenseLineStatusAsync(payment.ExpenseLine);

        return new
        {
            id = payment.Id,
            expenseLineId = payment.ExpenseLineId,
            expenseId = payment.ExpenseLine.ExpenseId,
            isApproved = dto.IsApproved
        };
    }

    public async Task DeletePaymentAsync(int id)
    {
        var payment = await _db.ExpenseLinePayments.FindAsync(id);
        if (payment == null)
            throw new KeyNotFoundException($"Payment with ID {id} not found.");

        if (payment.ApprovedAt != null)
            throw new ArgumentException("Only pending payments can be deleted.");

        var expenseLineId = payment.ExpenseLineId;

        _db.ExpenseLinePayments.Remove(payment);
        await _db.SaveChangesAsync();

        var expenseLine = await _db.ExpenseLines
            .Include(l => l.Payments)
            .FirstOrDefaultAsync(l => l.Id == expenseLineId);

        if (expenseLine != null)
        {
            await UpdateExpenseLineStatusAsync(expenseLine);
        }

    }
}
