using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace M2Manager.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceLineItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OcrLineItemsJson",
                table: "Invoices",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OcrLineItemsJson",
                table: "Invoices");
        }
    }
}
