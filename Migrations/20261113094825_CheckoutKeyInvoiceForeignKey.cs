using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stockify.Migrations
{
    /// <inheritdoc />
    public partial class CheckoutKeyInvoiceForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_CheckoutKeys_InvoiceId",
                table: "CheckoutKeys",
                column: "InvoiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_CheckoutKeys_Invoices_InvoiceId",
                table: "CheckoutKeys",
                column: "InvoiceId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CheckoutKeys_Invoices_InvoiceId",
                table: "CheckoutKeys");

            migrationBuilder.DropIndex(
                name: "IX_CheckoutKeys_InvoiceId",
                table: "CheckoutKeys");
        }
    }
}
