using System.Reflection;

namespace Stockify.Data;

/// <summary>
/// The EF Core database. Entity mappings live in Configurations/, seed data in Seeds/.
/// Every tenant-owned entity gets a global query filter on the current tenant and
/// SaveChanges stamps / guards TenantId, so tenants can never see or change each other's rows.
/// </summary>
public class AppDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Read by the tenant query filters on every query. 0 never matches a real tenant,
    /// so requests without a tenant see no tenant data.
    /// </summary>
    public int CurrentTenantId => _tenantContext.TenantId ?? 0;

    // Tenancy (global)
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();

    // Identity
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Organization
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<DeliveryLocation> DeliveryLocations => Set<DeliveryLocation>();

    // Catalog
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    // Pricing
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
    public DbSet<Currency> Currencies => Set<Currency>();

    // Inventory
    public DbSet<InventoryLot> InventoryLots => Set<InventoryLot>();
    public DbSet<BranchLotStock> BranchLotStocks => Set<BranchLotStock>();
    public DbSet<LocationStock> LocationStocks => Set<LocationStock>();
    public DbSet<InventoryAdjustment> InventoryAdjustments => Set<InventoryAdjustment>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferItem> StockTransferItems => Set<StockTransferItem>();
    public DbSet<StockTransferItemLot> StockTransferItemLots => Set<StockTransferItemLot>();

    // Sales
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderItemLot> OrderItemLots => Set<OrderItemLot>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<CreditMemo> CreditMemos => Set<CreditMemo>();
    public DbSet<CreditMemoItem> CreditMemoItems => Set<CreditMemoItem>();

    // Expenses
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseLine> ExpenseLines => Set<ExpenseLine>();
    public DbSet<ExpenseLinePayment> ExpenseLinePayments => Set<ExpenseLinePayment>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();

    // Lookup tables
    public DbSet<OrderStatus> OrderStatuses => Set<OrderStatus>();
    public DbSet<OrderType> OrderTypes => Set<OrderType>();
    public DbSet<InvoiceStatus> InvoiceStatuses => Set<InvoiceStatus>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<MovementType> MovementTypes => Set<MovementType>();
    public DbSet<StockTransferStatus> StockTransferStatuses => Set<StockTransferStatus>();
    public DbSet<SaleType> SaleTypes => Set<SaleType>();
    public DbSet<DiscountType> DiscountTypes => Set<DiscountType>();
    public DbSet<TenantStatus> TenantStatuses => Set<TenantStatus>();
    public DbSet<BusinessType> BusinessTypes => Set<BusinessType>();
    public DbSet<ExpenseLineType> ExpenseLineTypes => Set<ExpenseLineType>();
    public DbSet<ExpenseStatus> ExpenseStatuses => Set<ExpenseStatus>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Register every entity and lookup table (properties above are expression-bodied,
        // so EF discovers the types here instead of from DbSet auto-properties).
        foreach (var type in typeof(AppDbContext).GetProperties()
                     .Select(property => property.PropertyType)
                     .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DbSet<>))
                     .Select(type => type.GetGenericArguments()[0]))
        {
            modelBuilder.Entity(type);
        }

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Lookup tables: stable unique codes, short display fields.
        var configureLookup = typeof(AppDbContext).GetMethod(
            nameof(ConfigureLookup), BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(type => typeof(LookupEntity).IsAssignableFrom(type.ClrType))
                     .ToList())
        {
            configureLookup.MakeGenericMethod(entityType.ClrType).Invoke(null, new object[] { modelBuilder });
        }

        // Tenant-owned entities: FK to Tenants (indexed) + a global query filter.
        var applyTenantFilter = typeof(AppDbContext).GetMethod(
            nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(type => typeof(ITenantOwned).IsAssignableFrom(type.ClrType))
                     .ToList())
        {
            modelBuilder.Entity(entityType.ClrType)
                .HasOne(typeof(Tenant))
                .WithMany()
                .HasForeignKey(nameof(ITenantOwned.TenantId))
                .OnDelete(DeleteBehavior.Restrict);
            applyTenantFilter.MakeGenericMethod(entityType.ClrType).Invoke(this, new object[] { modelBuilder });
        }

        LookupSeeds.Apply(modelBuilder);
        IdentitySeeds.Apply(modelBuilder);
    }

    private static void ConfigureLookup<TLookup>(ModelBuilder modelBuilder) where TLookup : LookupEntity
    {
        modelBuilder.Entity<TLookup>(builder =>
        {
            builder.Property(lookup => lookup.Code).IsRequired().HasMaxLength(50);
            builder.HasIndex(lookup => lookup.Code).IsUnique();
            builder.Property(lookup => lookup.Name).IsRequired().HasMaxLength(100);
            builder.Property(lookup => lookup.Tone).IsRequired().HasMaxLength(20);
        });
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => entity.TenantId == CurrentTenantId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyTenantRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Stamps the current tenant on new rows and refuses to change or delete rows
    /// owned by another tenant, whatever TenantId a caller put on the entity.
    /// </summary>
    private void ApplyTenantRules()
    {
        var tenantId = _tenantContext.TenantId;

        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State == EntityState.Added)
            {
                if (tenantId is null)
                    throw new TenantAccessException("No tenant is selected for this operation.");
                entry.Entity.TenantId = tenantId.Value;
                continue;
            }

            if (entry.State is not (EntityState.Modified or EntityState.Deleted))
                continue;

            var tenantProperty = entry.Property(entity => entity.TenantId);
            if (tenantId is null || tenantProperty.OriginalValue != tenantId.Value)
                throw new TenantAccessException("This record belongs to another tenant.");

            tenantProperty.CurrentValue = tenantProperty.OriginalValue;
            tenantProperty.IsModified = false;
        }
    }
}
