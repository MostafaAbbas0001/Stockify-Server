namespace Stockify.Services;

public class UserService
{
    private const int PageSize = 20;
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly PasswordHasher _passwordHasher;

    public UserService(AppDbContext db, ITenantContext tenantContext, PasswordHasher passwordHasher)
    {
        _db = db;
        _tenantContext = tenantContext;
        _passwordHasher = passwordHasher;
    }

    public async Task<object> UpdateUserAsync(int id, UpdateUserRequest dto)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null)
            throw new KeyNotFoundException("User not found.");

        var role = await _db.Roles.FindAsync(dto.RoleId);
        if (role is null)
            throw new ArgumentException("Invalid RoleId.");

        var branch = await _db.Branches.FindAsync(dto.BranchId);
        if (branch is null)
            throw new ArgumentException("Invalid BranchId.");

        var email = AuthService.NormalizeEmail(dto.Email);
        await EnsureEmailAvailableAsync(email, excludingUserId: id);

        if (await IsTenantOwnerAsync(id) && user.RoleId != dto.RoleId)
            throw new ArgumentException("The company owner's role cannot be changed.");

        user.Email = email;
        user.Username = dto.Username.Trim();
        user.RoleId = dto.RoleId;
        user.AssignedRole = role;
        user.BranchId = dto.BranchId;
        user.AssignedBranch = branch;

        if (!string.IsNullOrWhiteSpace(dto.Password))
        {
            user.PasswordHash = _passwordHasher.Hash(dto.Password);
        }

        await _db.SaveChangesAsync();

        return new
        {
            user.Id,
            user.Email,
            user.Username,
            Role = role.Name,
            user.RoleId,
            user.BranchId
        };
    }

    public async Task<object> CreateUserAsync(CreateUserRequest dto)
    {
        var user = await CreateUserEntityAsync(dto);
        return new
        {
            user.Id,
            user.Email,
            user.Username,
            Role = user.AssignedRole.Name
        };
    }

    /// <summary>
    /// Creates a user in the current tenant with one permission row per permission,
    /// active when the role grants it by default.
    /// </summary>
    public async Task<User> CreateUserEntityAsync(CreateUserRequest dto)
    {
        var email = AuthService.NormalizeEmail(dto.Email);
        await EnsureEmailAvailableAsync(email, excludingUserId: null);

        var role = await _db.Roles.FindAsync(dto.RoleId);
        if (role is null)
            throw new ArgumentException("Invalid RoleId.");

        var branch = await _db.Branches.FindAsync(dto.BranchId);
        if (branch is null)
            throw new ArgumentException($"Invalid BranchId: {dto.BranchId}. Branch does not exist.");

        var user = new User
        {
            Email = email,
            Username = dto.Username.Trim(),
            PasswordHash = _passwordHasher.Hash(dto.Password),
            RoleId = dto.RoleId,
            BranchId = dto.BranchId,
            AssignedRole = role
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var allPermissions = await _db.Permissions.Select(p => new { p.Id }).ToListAsync();
        var rolePermissionIds = new HashSet<int>(await _db.RolePermissions
            .Where(rp => rp.RoleId == role.Id)
            .Select(rp => rp.PermissionId)
            .ToListAsync());

        var userPermissions = allPermissions.Select(p => new UserPermission
        {
            UserId = user.Id,
            PermissionId = p.Id,
            IsActive = rolePermissionIds.Contains(p.Id)
        }).ToList();

        _db.UserPermissions.AddRange(userPermissions);
        await _db.SaveChangesAsync();

        return user;
    }

    public async Task<object> GetAllUsersAsync(
        string? search,
        int? roleId,
        int? branchId,
        int page)
    {
        page = Math.Max(page, 1);
        var ownerUserId = await GetTenantOwnerIdAsync();
        var query = _db.Users
            .AsNoTracking()
            .Include(user => user.AssignedRole)
            .Include(user => user.AssignedBranch)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().ToLower()}%";
            query = query.Where(user =>
                EF.Functions.Like(user.Username.ToLower(), pattern) ||
                EF.Functions.Like(user.Email.ToLower(), pattern) ||
                EF.Functions.Like(user.AssignedRole.Name.ToLower(), pattern) ||
                EF.Functions.Like(user.AssignedBranch.Name.ToLower(), pattern));
        }

        if (roleId.HasValue)
            query = query.Where(user => user.RoleId == roleId.Value);
        if (branchId.HasValue)
            query = query.Where(user => user.BranchId == branchId.Value);

        var totalCount = await query.CountAsync();
        var users = await query
            .OrderByDescending(user => user.CreatedAt)
            .ThenByDescending(user => user.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.Username,
                Role = user.AssignedRole.Name,
                user.RoleId,
                Branch = user.AssignedBranch.Name,
                user.BranchId,
                user.CreatedAt,
                IsOwner = user.Id == ownerUserId
            })
            .ToListAsync();

        return new
        {
            totalCount,
            page,
            perPage = PageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)PageSize),
            users
        };
    }

    public async Task<object> GetByIdAsync(int id)
    {
        var user = await _db.Users
            .Include(user => user.AssignedRole)
            .Include(user => user.AssignedBranch)
            .SingleOrDefaultAsync(user => user.Id == id);

        if (user is null)
            throw new KeyNotFoundException("User not found.");

        var result = new
        {
            user.Id,
            user.Email,
            user.Username,
            Role = user.AssignedRole.Name,
            user.RoleId,
            user.BranchId,
            Branch = user.AssignedBranch.Name,
            IsOwner = await IsTenantOwnerAsync(user.Id)
        };

        return result;
    }

    public async Task<IEnumerable<object>> GetRolesAsync()
    {
        return await _db.Roles
            .Select(role => new
            {
                role.Id,
                role.Name
            })
            .ToListAsync();
    }

    public async Task<bool> DeleteUserAsync(int id, int currentUserId)
    {
        var user = await _db.Users.FindAsync(id);

        if (user is null)
            throw new KeyNotFoundException("User not found.");

        if (user.Id == currentUserId)
            throw new ArgumentException("You cannot delete your own account.");

        if (await IsTenantOwnerAsync(user.Id))
            throw new ArgumentException("The company owner cannot be deleted.");

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        return true;
    }

    public async Task<object> GetUserPermissionsAsync(int id)
    {
        var user = await _db.Users
            .Include(u => u.AssignedRole)
            .Include(u => u.UserPermissions)
                .ThenInclude(up => up.Permission)
            .SingleOrDefaultAsync(u => u.Id == id);

        if (user is null)
            throw new KeyNotFoundException("User not found.");

        var roleDefaultIds = await _db.RolePermissions
            .Where(rp => rp.RoleId == user.RoleId)
            .Select(rp => rp.PermissionId)
            .ToListAsync();

        var roleDefaultSet = new HashSet<int>(roleDefaultIds);

        var overrides = user.UserPermissions.ToDictionary(x => x.PermissionId, x => (bool?)x.IsActive);

        var allPerms = await _db.Permissions
            .Select(p => new
            {
                p.Id,
                p.Key,
                p.Target
            })
            .ToListAsync();

        var perms = allPerms
            .Select(p => new
            {
                id = p.Id,
                key = p.Key,
                target = p.Target,
                roleDefault = roleDefaultSet.Contains(p.Id),
                userOverride = overrides.ContainsKey(p.Id) ? overrides[p.Id] : (bool?)null,
                effective = (overrides.ContainsKey(p.Id) ? overrides[p.Id]!.Value : roleDefaultSet.Contains(p.Id))
            })
            .OrderBy(p => p.key)
            .ToList();

        var result = new
        {
            userId = user.Id,
            roleId = user.RoleId,
            roleName = user.AssignedRole.Name,
            permissions = perms
        };

        return result;
    }

    public async Task<bool> UpdateUserPermissionsAsync(
        int id,
        UpdateUserPermissionsRequest dto,
        int currentUserId)
    {
        if (id == currentUserId)
            throw new ArgumentException("You cannot change your own permissions.");

        var user = await _db.Users.FindAsync(id);
        if (user is null)
            throw new KeyNotFoundException("User not found.");

        if (await IsTenantOwnerAsync(id))
            throw new ArgumentException("The company owner's permissions cannot be changed.");

        var requestedIds = dto.Items.Select(item => item.PermissionId).Distinct().ToList();
        var knownIds = await _db.Permissions
            .Where(permission => requestedIds.Contains(permission.Id))
            .Select(permission => permission.Id)
            .ToListAsync();
        var unknownIds = requestedIds.Except(knownIds).ToList();
        if (unknownIds.Count > 0)
            throw new ArgumentException($"Unknown permission ids: {string.Join(", ", unknownIds)}.");

        var currentPerms = await _db.UserPermissions
            .Where(up => up.UserId == id)
            .ToDictionaryAsync(up => up.PermissionId, up => up);

        foreach (var item in dto.Items)
        {
            var permId = item.PermissionId;
            var isActive = item.IsActive;

            if (currentPerms.ContainsKey(permId))
            {
                currentPerms[permId].IsActive = isActive;
            }
            else
            {
                _db.UserPermissions.Add(new UserPermission
                {
                    UserId = id,
                    PermissionId = permId,
                    IsActive = isActive
                });
            }
        }

        await _db.SaveChangesAsync();
        return true;
    }

    private async Task EnsureEmailAvailableAsync(string email, int? excludingUserId)
    {
        // Emails are the login identity across every tenant, so check globally.
        var taken = await _db.Users
            .IgnoreQueryFilters()
            .AnyAsync(user => user.Email == email && user.Id != excludingUserId);
        if (taken)
            throw new InvalidOperationException("This email is already registered.");
    }

    private async Task<int?> GetTenantOwnerIdAsync()
    {
        var tenantId = _tenantContext.TenantId;
        return await _db.Tenants
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.OwnerUserId)
            .FirstOrDefaultAsync();
    }

    private async Task<bool> IsTenantOwnerAsync(int userId) =>
        await GetTenantOwnerIdAsync() == userId;
}
