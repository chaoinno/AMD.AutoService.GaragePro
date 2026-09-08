using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Add_Supplier_Warehouse_Category : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "svc_CatalogItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "svc_CatalogItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "svc_CatalogCategory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ParentCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_CatalogCategory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_svc_CatalogCategory_svc_CatalogCategory_ParentCategoryId",
                        column: x => x.ParentCategoryId,
                        principalTable: "svc_CatalogCategory",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "svc_Supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ContactName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TaxId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PaymentTerms = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_Supplier", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "svc_Warehouse",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_Warehouse", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "svc_CatalogItemSupplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierItemCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    SupplierCost = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LeadTimeDays = table.Column<int>(type: "int", nullable: true),
                    MinOrderQty = table.Column<int>(type: "int", nullable: true),
                    IsPreferred = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_CatalogItemSupplier", x => x.Id);
                    table.ForeignKey(
                        name: "FK_svc_CatalogItemSupplier_svc_CatalogItem_CatalogItemId",
                        column: x => x.CatalogItemId,
                        principalTable: "svc_CatalogItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_svc_CatalogItemSupplier_svc_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "svc_Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_svc_CatalogItem_CategoryId",
                table: "svc_CatalogItem",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_CatalogItem_WarehouseId",
                table: "svc_CatalogItem",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_CatalogCategory_ParentCategoryId",
                table: "svc_CatalogCategory",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "UX_svc_CatalogCategory_Code",
                table: "svc_CatalogCategory",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_CatalogItemSupplier_SupplierId",
                table: "svc_CatalogItemSupplier",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "UX_svc_CatalogItemSupplier_Item_Supplier",
                table: "svc_CatalogItemSupplier",
                columns: new[] { "CatalogItemId", "SupplierId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_svc_CatalogItemSupplier_Preferred",
                table: "svc_CatalogItemSupplier",
                column: "CatalogItemId",
                unique: true,
                filter: "[IsPreferred] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_svc_Supplier_Code",
                table: "svc_Supplier",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_svc_Warehouse_Code",
                table: "svc_Warehouse",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_svc_CatalogItem_svc_CatalogCategory_CategoryId",
                table: "svc_CatalogItem",
                column: "CategoryId",
                principalTable: "svc_CatalogCategory",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_svc_CatalogItem_svc_Warehouse_WarehouseId",
                table: "svc_CatalogItem",
                column: "WarehouseId",
                principalTable: "svc_Warehouse",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_svc_CatalogItem_svc_CatalogCategory_CategoryId",
                table: "svc_CatalogItem");

            migrationBuilder.DropForeignKey(
                name: "FK_svc_CatalogItem_svc_Warehouse_WarehouseId",
                table: "svc_CatalogItem");

            migrationBuilder.DropTable(
                name: "svc_CatalogCategory");

            migrationBuilder.DropTable(
                name: "svc_CatalogItemSupplier");

            migrationBuilder.DropTable(
                name: "svc_Warehouse");

            migrationBuilder.DropTable(
                name: "svc_Supplier");

            migrationBuilder.DropIndex(
                name: "IX_svc_CatalogItem_CategoryId",
                table: "svc_CatalogItem");

            migrationBuilder.DropIndex(
                name: "IX_svc_CatalogItem_WarehouseId",
                table: "svc_CatalogItem");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "svc_CatalogItem");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "svc_CatalogItem");
        }
    }
}
