using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stockify.Migrations
{
    /// <inheritdoc />
    public partial class TenantDateIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_CreatedAt",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_TenantId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_CreatedAt",
                table: "ProductVariants");

            migrationBuilder.DropIndex(
                name: "IX_Products_CreatedAt",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_TenantId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CreatedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_IssuedAt",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_InventoryLots_ReceivedAt",
                table: "InventoryLots");

            migrationBuilder.DropIndex(
                name: "IX_InventoryLots_TenantId",
                table: "InventoryLots");

            migrationBuilder.DropIndex(
                name: "IX_Customers_CreatedAt",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_TenantId",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_TenantId_CreatedAt",
                table: "StockMovements",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_TenantId_CreatedAt",
                table: "ProductVariants",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_CreatedAt",
                table: "Products",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_CreatedAt",
                table: "Orders",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_IssuedAt",
                table: "Invoices",
                columns: new[] { "TenantId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLots_TenantId_ReceivedAt",
                table: "InventoryLots",
                columns: new[] { "TenantId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_CreatedAt",
                table: "Customers",
                columns: new[] { "TenantId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_TenantId_CreatedAt",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_TenantId_CreatedAt",
                table: "ProductVariants");

            migrationBuilder.DropIndex(
                name: "IX_Products_TenantId_CreatedAt",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId_CreatedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_TenantId_IssuedAt",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_InventoryLots_TenantId_ReceivedAt",
                table: "InventoryLots");

            migrationBuilder.DropIndex(
                name: "IX_Customers_TenantId_CreatedAt",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CreatedAt",
                table: "StockMovements",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_TenantId",
                table: "StockMovements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_CreatedAt",
                table: "ProductVariants",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CreatedAt",
                table: "Products",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId",
                table: "Products",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedAt",
                table: "Orders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_IssuedAt",
                table: "Invoices",
                column: "IssuedAt");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLots_ReceivedAt",
                table: "InventoryLots",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLots_TenantId",
                table: "InventoryLots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CreatedAt",
                table: "Customers",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId",
                table: "Customers",
                column: "TenantId");
        }
    }
}
