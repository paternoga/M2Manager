using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace M2Manager.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExpenseCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Properties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    TotalAreaM2 = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    DefaultRoomHeightM = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 2.60m)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Properties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShoppingCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rooms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PropertyId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    FloorAreaM2 = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    LengthM = table.Column<decimal>(type: "numeric(8,3)", nullable: true),
                    WidthM = table.Column<decimal>(type: "numeric(8,3)", nullable: true),
                    HeightM = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    ManualWallAreaM2 = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    ExcludedWallAreaM2 = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    ManualCeilingAreaM2 = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    IncludeInTotals = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    GeometryJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rooms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rooms_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PropertyId = table.Column<int>(type: "integer", nullable: false),
                    RoomId = table.Column<int>(type: "integer", nullable: true),
                    ExpenseCategoryId = table.Column<int>(type: "integer", nullable: true),
                    Vendor = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "PLN"),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ImageObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OcrStatus = table.Column<int>(type: "integer", nullable: false),
                    OcrRawResponse = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_ExpenseCategories_ExpenseCategoryId",
                        column: x => x.ExpenseCategoryId,
                        principalTable: "ExpenseCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Invoices_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Invoices_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RoomOpenings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoomId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    WidthCm = table.Column<decimal>(type: "numeric(8,1)", nullable: false),
                    HeightCm = table.Column<decimal>(type: "numeric(8,1)", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    SubtractFromWalls = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    WallSide = table.Column<int>(type: "integer", nullable: true),
                    OffsetCm = table.Column<decimal>(type: "numeric(8,1)", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomOpenings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoomOpenings_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShoppingItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrdinalNo = table.Column<int>(type: "integer", nullable: false),
                    PropertyId = table.Column<int>(type: "integer", nullable: false),
                    RoomId = table.Column<int>(type: "integer", nullable: true),
                    ShoppingCategoryId = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CalculationNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(12,3)", nullable: true),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    UnitCost = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    TotalCost = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    PlannedBudget = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    ActualCost = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    Vendor = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Link = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    PurchaseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InvoiceId = table.Column<int>(type: "integer", nullable: true),
                    AssignedTo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShoppingItems_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ShoppingItems_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShoppingItems_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ShoppingItems_ShoppingCategories_ShoppingCategoryId",
                        column: x => x.ShoppingCategoryId,
                        principalTable: "ShoppingCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_Name",
                table: "ExpenseCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ExpenseCategoryId",
                table: "Invoices",
                column: "ExpenseCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PropertyId_IssueDate",
                table: "Invoices",
                columns: new[] { "PropertyId", "IssueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_RoomId",
                table: "Invoices",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_Name",
                table: "Properties",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_RoomOpenings_RoomId",
                table: "RoomOpenings",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_PropertyId_SortOrder",
                table: "Rooms",
                columns: new[] { "PropertyId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingCategories_Name",
                table: "ShoppingCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingItems_InvoiceId",
                table: "ShoppingItems",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingItems_PropertyId_OrdinalNo",
                table: "ShoppingItems",
                columns: new[] { "PropertyId", "OrdinalNo" });

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingItems_RoomId",
                table: "ShoppingItems",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingItems_ShoppingCategoryId",
                table: "ShoppingItems",
                column: "ShoppingCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingItems_Status",
                table: "ShoppingItems",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoomOpenings");

            migrationBuilder.DropTable(
                name: "ShoppingItems");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "ShoppingCategories");

            migrationBuilder.DropTable(
                name: "ExpenseCategories");

            migrationBuilder.DropTable(
                name: "Rooms");

            migrationBuilder.DropTable(
                name: "Properties");
        }
    }
}
