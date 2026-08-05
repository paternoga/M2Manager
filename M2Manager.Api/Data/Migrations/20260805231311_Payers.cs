using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace M2Manager.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Payers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PayerId",
                table: "ShoppingItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayerId",
                table: "Invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Payers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingItems_PayerId",
                table: "ShoppingItems",
                column: "PayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PayerId",
                table: "Invoices",
                column: "PayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Payers_Name",
                table: "Payers",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Payers_PayerId",
                table: "Invoices",
                column: "PayerId",
                principalTable: "Payers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ShoppingItems_Payers_PayerId",
                table: "ShoppingItems",
                column: "PayerId",
                principalTable: "Payers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Payers_PayerId",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_ShoppingItems_Payers_PayerId",
                table: "ShoppingItems");

            migrationBuilder.DropTable(
                name: "Payers");

            migrationBuilder.DropIndex(
                name: "IX_ShoppingItems_PayerId",
                table: "ShoppingItems");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_PayerId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PayerId",
                table: "ShoppingItems");

            migrationBuilder.DropColumn(
                name: "PayerId",
                table: "Invoices");
        }
    }
}
