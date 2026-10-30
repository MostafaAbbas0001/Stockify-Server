using Microsoft.EntityFrameworkCore;

namespace Stockify.Data;

/// <summary>Global roles, permissions and each role's default permissions.</summary>
public static class IdentitySeeds
{
    private const int ExecutiveAdminRoleId = 1;

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>().HasData(
            new Role { Id = ExecutiveAdminRoleId, Code = RoleCodes.ExecutiveAdmin, Name = "ExecutiveAdmin" },
            new Role { Id = 2, Code = RoleCodes.OrderManager, Name = "OrderManager" },
            new Role { Id = 3, Code = RoleCodes.StockManager, Name = "StockManager" },
            new Role { Id = 4, Code = RoleCodes.PointOfSales, Name = "PointOfSales" }
        );

        // Backend-route keys gate API calls; "UI tab" keys only control client navigation.
        var permissions = new[]
        {
            new Permission { Id = 1, Key = "brand.view", Target = "Backend route" },
            new Permission { Id = 2, Key = "brand.create", Target = "Backend route" },
            new Permission { Id = 3, Key = "brand.update", Target = "Backend route" },
            new Permission { Id = 4, Key = "brand.delete", Target = "Backend route" },
            new Permission { Id = 5, Key = "product.view", Target = "Backend route" },
            new Permission { Id = 6, Key = "product.create", Target = "Backend route" },
            new Permission { Id = 7, Key = "product.update", Target = "Backend route" },
            new Permission { Id = 8, Key = "product.delete", Target = "Backend route" },
            new Permission { Id = 9, Key = "variant.create", Target = "Backend route" },
            new Permission { Id = 10, Key = "variant.view", Target = "Backend route" },
            new Permission { Id = 11, Key = "variant.delete", Target = "Backend route" },
            new Permission { Id = 12, Key = "variant.update", Target = "Backend route" },
            new Permission { Id = 13, Key = "category.view", Target = "Backend route" },
            new Permission { Id = 14, Key = "category.create", Target = "Backend route" },
            new Permission { Id = 15, Key = "category.update", Target = "Backend route" },
            new Permission { Id = 16, Key = "category.delete", Target = "Backend route" },
            new Permission { Id = 17, Key = "attribute.view", Target = "Backend route" },
            new Permission { Id = 18, Key = "attribute.create", Target = "Backend route" },
            new Permission { Id = 19, Key = "attribute.update", Target = "Backend route" },
            new Permission { Id = 20, Key = "attribute.delete", Target = "Backend route" },
            new Permission { Id = 21, Key = "attributeValue.view", Target = "Backend route" },
            new Permission { Id = 22, Key = "attributeValue.create", Target = "Backend route" },
            new Permission { Id = 23, Key = "attributeValue.update", Target = "Backend route" },
            new Permission { Id = 24, Key = "attributeValue.delete", Target = "Backend route" },
            new Permission { Id = 25, Key = "deliveryCharge.view", Target = "Backend route" },
            new Permission { Id = 26, Key = "deliveryCharge.create", Target = "Backend route" },
            new Permission { Id = 27, Key = "deliveryCharge.update", Target = "Backend route" },
            new Permission { Id = 28, Key = "deliveryCharge.delete", Target = "Backend route" },
            new Permission { Id = 29, Key = "pos.access", Target = "Backend route" },
            new Permission { Id = 30, Key = "order.create", Target = "Backend route" },
            new Permission { Id = 31, Key = "order.view", Target = "Backend route" },
            new Permission { Id = 32, Key = "order.cancel", Target = "Backend route" },
            new Permission { Id = 33, Key = "invoice.view", Target = "Backend route" },
            new Permission { Id = 34, Key = "invoice.payment.add", Target = "Backend route" },
            new Permission { Id = 35, Key = "invoice.return", Target = "Backend route" },
            new Permission { Id = 36, Key = "sale.view", Target = "Backend route" },
            new Permission { Id = 37, Key = "sale.create", Target = "Backend route" },
            new Permission { Id = 38, Key = "sale.update", Target = "Backend route" },
            new Permission { Id = 39, Key = "sale.delete", Target = "Backend route" },
            new Permission { Id = 40, Key = "sale.assignVariant", Target = "Backend route" },
            new Permission { Id = 41, Key = "stock.view", Target = "Backend route" },
            new Permission { Id = 42, Key = "stock.restock", Target = "Backend route" },
            new Permission { Id = 43, Key = "stock.reduce", Target = "Backend route" },
            new Permission { Id = 44, Key = "stockMovement.view", Target = "Backend route" },
            new Permission { Id = 45, Key = "reports.stockInHand.view", Target = "Backend route" },
            new Permission { Id = 46, Key = "users.view", Target = "Backend route" },
            new Permission { Id = 47, Key = "users.create", Target = "Backend route" },
            new Permission { Id = 48, Key = "users.update", Target = "Backend route" },
            new Permission { Id = 49, Key = "users.delete", Target = "Backend route" },
            new Permission { Id = 50, Key = "branch.create", Target = "Backend route" },
            new Permission { Id = 51, Key = "branch.update", Target = "Backend route" },
            new Permission { Id = 52, Key = "branch.view", Target = "Backend route" },
            new Permission { Id = 53, Key = "branch.delete", Target = "Backend route" },
            new Permission { Id = 54, Key = "stockTransfer.request", Target = "Backend route" },
            new Permission { Id = 55, Key = "stockTransfer.send", Target = "Backend route" },
            new Permission { Id = 56, Key = "stockTransfer.receive", Target = "Backend route" },
            new Permission { Id = 57, Key = "stockTransfer.cancel", Target = "Backend route" },
            new Permission { Id = 58, Key = "stockTransfer.view", Target = "Backend route" },
            new Permission { Id = 59, Key = "stockTransfer.resolve", Target = "Backend route" },
            new Permission { Id = 60, Key = "employee.create", Target = "Backend route" },
            new Permission { Id = 61, Key = "employee.view", Target = "Backend route" },
            new Permission { Id = 62, Key = "employee.update", Target = "Backend route" },
            new Permission { Id = 63, Key = "employee.delete", Target = "Backend route" },
            new Permission { Id = 64, Key = "employee.tab.view", Target = "Backend route" },
            new Permission { Id = 65, Key = "expenseDecision.submit", Target = "Backend route" },
            new Permission { Id = 66, Key = "expense.create", Target = "Backend route" },
            new Permission { Id = 67, Key = "expense.view", Target = "Backend route" },
            new Permission { Id = 68, Key = "invoice.payment.submit", Target = "Backend route" },
            new Permission { Id = 69, Key = "invoice.create", Target = "Backend route" },
            new Permission { Id = 70, Key = "customer.view", Target = "Backend route" },
            new Permission { Id = 71, Key = "customer.create", Target = "Backend route" },
            new Permission { Id = 72, Key = "customer.update", Target = "Backend route" },
            new Permission { Id = 73, Key = "customer.delete", Target = "Backend route" },
            new Permission { Id = 74, Key = "currency.view", Target = "Backend route" },
            new Permission { Id = 75, Key = "dashboard.tab.view", Target = "UI tab" },
            new Permission { Id = 76, Key = "pos.tab.view", Target = "UI tab" },
            new Permission { Id = 77, Key = "orders.tab.view", Target = "UI tab" },
            new Permission { Id = 78, Key = "invoices.tab.view", Target = "UI tab" },
            new Permission { Id = 79, Key = "sales.tab.view", Target = "UI tab" },
            new Permission { Id = 80, Key = "brands.tab.view", Target = "UI tab" },
            new Permission { Id = 81, Key = "categories.tab.view", Target = "UI tab" },
            new Permission { Id = 82, Key = "products.tab.view", Target = "UI tab" },
            new Permission { Id = 83, Key = "attributes.tab.view", Target = "UI tab" },
            new Permission { Id = 84, Key = "attributeValues.tab.view", Target = "UI tab" },
            new Permission { Id = 85, Key = "stock.tab.view", Target = "UI tab" },
            new Permission { Id = 86, Key = "stockTransfer.tab.view", Target = "UI tab" },
            new Permission { Id = 87, Key = "stockMovement.tab.view", Target = "UI tab" },
            new Permission { Id = 88, Key = "employees.tab.view", Target = "UI tab" },
            new Permission { Id = 89, Key = "customers.tab.view", Target = "UI tab" },
            new Permission { Id = 90, Key = "delivery.tab.view", Target = "UI tab" },
            new Permission { Id = 91, Key = "expenses.tab.view", Target = "UI tab" },
            new Permission { Id = 92, Key = "branches.tab.view", Target = "UI tab" },
            new Permission { Id = 93, Key = "reports.tab.view", Target = "UI tab" },
            new Permission { Id = 94, Key = "users.tab.view", Target = "UI tab" },
            // Lets a user see and filter data of every branch in their company, not only their own.
            new Permission { Id = 95, Key = "branch.all.access", Target = "Backend route" }
        };
        modelBuilder.Entity<Permission>().HasData(permissions);

        // ExecutiveAdmin gets every permission by default. The other roles have no defaults yet;
        // their users get permissions individually through the user permission editor.
        modelBuilder.Entity<RolePermission>().HasData(permissions
            .Select(permission => new RolePermission { RoleId = ExecutiveAdminRoleId, PermissionId = permission.Id })
            .ToArray());
    }
}
