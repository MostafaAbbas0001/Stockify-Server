using System.Text.RegularExpressions;

namespace Stockify.Services;

/// <summary>Creates and manages tenants (companies). Used only by platform admins.</summary>
public class TenantProvisioningService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly UserService _userService;
    private readonly LookupService _lookups;
    private readonly TenantStatusService _tenantStatus;

    public TenantProvisioningService(
        AppDbContext db,
        ITenantContext tenantContext,
        UserService userService,
        LookupService lookups,
        TenantStatusService tenantStatus)
    {
        _db = db;
        _tenantContext = tenantContext;
        _userService = userService;
        _lookups = lookups;
        _tenantStatus = tenantStatus;
    }

    /// <summary>
    /// Creates a tenant with its first branch, default tax rate, currencies, in-store
    /// delivery location, walk-in customer, expense categories and owner account.
    /// </summary>
    public async Task<object> CreateTenantAsync(CreateTenantRequest dto)
    {
        var businessTypes = await _lookups.ListAsync<BusinessType>();
        var businessTypeCode = dto.BusinessType.Trim().ToUpperInvariant();
        var businessType = businessTypes.FirstOrDefault(type => type.Code == businessTypeCode)
            ?? throw new ArgumentException(
                $"BusinessType must be one of: {string.Join(", ", businessTypes.Select(type => type.Code))}.");
        var executiveAdminRoleId = await _db.Roles
            .Where(role => role.Code == RoleCodes.ExecutiveAdmin)
            .Select(role => role.Id)
            .SingleAsync();

        var baseCurrency = new CurrencyRequest
        {
            Code = dto.BaseCurrency.Code,
            Name = dto.BaseCurrency.Name,
            Symbol = dto.BaseCurrency.Symbol,
            ExchangeRate = 1
        };
        var currencies = new[] { baseCurrency }
            .Concat(dto.AdditionalCurrencies)
            .ToList();
        var duplicateCode = currencies
            .GroupBy(currency => currency.Code.Trim().ToUpperInvariant())
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateCode != null)
            throw new ArgumentException($"Currency {duplicateCode.Key} is listed more than once.");

        var slug = await GenerateUniqueSlugAsync(
            string.IsNullOrWhiteSpace(dto.Slug) ? dto.CompanyName : dto.Slug);

        await using var tx = await _db.Database.BeginTransactionAsync();

        var tenant = new Tenant
        {
            Name = dto.CompanyName.Trim(),
            Slug = slug,
            BusinessTypeId = businessType.Id,
            StatusId = await _lookups.IdAsync<TenantStatus>(TenantStatusCodes.Active),
            CreatedAt = DateTime.UtcNow
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        using (_tenantContext.UseTenant(tenant.Id))
        {
            var branch = new Branch
            {
                Name = string.IsNullOrWhiteSpace(dto.FirstBranchName) ? "Main Branch" : dto.FirstBranchName.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            _db.Branches.Add(branch);

            _db.TaxRates.Add(new TaxRate
            {
                Name = string.IsNullOrWhiteSpace(dto.TaxName) ? "VAT" : dto.TaxName.Trim(),
                Rate = dto.TaxRate,
                IsDefault = true
            });

            foreach (var currency in currencies)
            {
                _db.Currencies.Add(new Currency
                {
                    Code = currency.Code.Trim().ToUpperInvariant(),
                    Name = currency.Name.Trim(),
                    Symbol = currency.Symbol?.Trim(),
                    ExchangeRate = currency.ExchangeRate,
                    IsActive = true
                });
            }

            _db.DeliveryLocations.Add(new DeliveryLocation
            {
                LocationName = "In Store",
                Fee = 0m,
                IsInStore = true,
                CreatedAt = DateTime.UtcNow
            });

            _db.Customers.Add(new Customer
            {
                Name = "Walk-in Customer",
                Phone = "-",
                Address = "In-store purchase",
                Email = "-",
                CreatedAt = DateTime.UtcNow
            });

            var categoriesByCode = new Dictionary<string, ExpenseCategory>();
            foreach (var template in DefaultTenantData.ExpenseCategories)
            {
                var category = new ExpenseCategory
                {
                    Code = template.Code,
                    Name = template.Name,
                    Description = template.Description,
                    Parent = template.ParentCode == null ? null : categoriesByCode[template.ParentCode]
                };
                categoriesByCode[template.Code] = category;
                _db.ExpenseCategories.Add(category);
            }

            await _db.SaveChangesAsync();

            var owner = await _userService.CreateUserEntityAsync(new CreateUserRequest
            {
                Email = dto.OwnerEmail,
                Username = dto.OwnerName,
                Password = dto.OwnerPassword,
                RoleId = executiveAdminRoleId,
                BranchId = branch.Id
            });

            tenant.OwnerUserId = owner.Id;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return new
            {
                tenant.Id,
                tenant.Name,
                tenant.Slug,
                BusinessType = businessType.Code,
                Status = TenantStatusCodes.Active,
                OwnerUserId = owner.Id,
                OwnerEmail = owner.Email,
                FirstBranchId = branch.Id
            };
        }
    }

    public async Task<object> GetTenantsAsync()
    {
        var tenants = await _db.Tenants
            .AsNoTracking()
            .OrderByDescending(tenant => tenant.CreatedAt)
            .Select(tenant => new
            {
                tenant.Id,
                tenant.Name,
                tenant.Slug,
                BusinessType = tenant.BusinessType.Code,
                Status = tenant.Status.Code,
                tenant.OwnerUserId,
                OwnerEmail = _db.Users
                    .IgnoreQueryFilters()
                    .Where(user => user.Id == tenant.OwnerUserId)
                    .Select(user => user.Email)
                    .FirstOrDefault(),
                UserCount = _db.Users.IgnoreQueryFilters().Count(user => user.TenantId == tenant.Id),
                BranchCount = _db.Branches.IgnoreQueryFilters()
                    .Count(branch => branch.TenantId == tenant.Id && !branch.IsDeleted),
                tenant.CreatedAt
            })
            .ToListAsync();

        return tenants;
    }

    public async Task<object> SetStatusAsync(int tenantId, string status)
    {
        var statuses = await _lookups.ListAsync<TenantStatus>();
        var statusCode = (status ?? string.Empty).Trim().ToUpperInvariant();
        var newStatus = statuses.FirstOrDefault(item => item.Code == statusCode)
            ?? throw new ArgumentException(
                $"Status must be one of: {string.Join(", ", statuses.Select(item => item.Code))}.");

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId)
            ?? throw new KeyNotFoundException($"Tenant {tenantId} not found.");

        tenant.StatusId = newStatus.Id;

        if (newStatus.Code != TenantStatusCodes.Active)
        {
            // Force every user of the tenant to sign in again (which is then refused).
            var activeTokens = await _db.RefreshTokens
                .IgnoreQueryFilters()
                .Where(token => token.TenantId == tenantId && token.RevokedAt == null)
                .ToListAsync();
            using (_tenantContext.UseTenant(tenantId))
            {
                foreach (var token in activeTokens)
                    token.RevokedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }
        else
        {
            await _db.SaveChangesAsync();
        }

        _tenantStatus.Invalidate(tenantId);

        return new { tenant.Id, Status = newStatus.Code };
    }

    private async Task<string> GenerateUniqueSlugAsync(string source)
    {
        var baseSlug = Regex.Replace(source.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrEmpty(baseSlug))
            baseSlug = "company";
        if (baseSlug.Length > 90)
            baseSlug = baseSlug[..90].TrimEnd('-');

        var slug = baseSlug;
        var suffix = 2;
        while (await _db.Tenants.AnyAsync(tenant => tenant.Slug == slug))
            slug = $"{baseSlug}-{suffix++}";
        return slug;
    }
}
