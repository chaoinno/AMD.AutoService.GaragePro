using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRetailSaleAndPromotion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SaleId",
                table: "svc_StockMovement",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "svc_Promotion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    MinSubtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedByName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_Promotion", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "svc_Sale",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyCustomerId = table.Column<long>(type: "bigint", nullable: true),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CustomerPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    VatIncluded = table.Column<bool>(type: "bit", nullable: false),
                    BillDiscountType = table.Column<int>(type: "int", nullable: false),
                    BillDiscountValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BillPromotionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BillPromotionCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    BillPromotionName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineDiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LinePromotionAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SubtotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BillDiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BillPromotionAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CostTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReceiptNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CheckoutRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CheckoutRequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CompletedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    VoidedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoidRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_Sale", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "svc_SaleReceiptNumberCounter",
                columns: table => new
                {
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastSequence = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_SaleReceiptNumberCounter", x => new { x.LegacyShardKey, x.LegacyBranchId, x.Year });
                });

            migrationBuilder.CreateTable(
                name: "svc_SaleLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PromotionCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PromotionName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PromotionAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CostAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_SaleLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_svc_SaleLine_svc_CatalogItem_CatalogItemId",
                        column: x => x.CatalogItemId,
                        principalTable: "svc_CatalogItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_svc_SaleLine_svc_Sale_SaleId",
                        column: x => x.SaleId,
                        principalTable: "svc_Sale",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "svc_SalePayment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ReceivedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_SalePayment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_svc_SalePayment_svc_Sale_SaleId",
                        column: x => x.SaleId,
                        principalTable: "svc_Sale",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_svc_StockMovement_LegacyShardKey_LegacyBranchId_SaleId",
                table: "svc_StockMovement",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "SaleId" },
                filter: "[SaleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_svc_Promotion_Code",
                table: "svc_Promotion",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_Sale_LegacyShardKey_LegacyBranchId_CheckoutRequestId",
                table: "svc_Sale",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "CheckoutRequestId" },
                unique: true,
                filter: "[CheckoutRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_svc_Sale_LegacyShardKey_LegacyBranchId_ReceiptNo",
                table: "svc_Sale",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "ReceiptNo" },
                unique: true,
                filter: "[ReceiptNo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_svc_Sale_LegacyShardKey_LegacyBranchId_Status_CreatedAt",
                table: "svc_Sale",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_SaleLine_CatalogItemId",
                table: "svc_SaleLine",
                column: "CatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_SaleLine_SaleId_CatalogItemId",
                table: "svc_SaleLine",
                columns: new[] { "SaleId", "CatalogItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_SalePayment_SaleId",
                table: "svc_SalePayment",
                column: "SaleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_Promotion");

            migrationBuilder.DropTable(
                name: "svc_SaleLine");

            migrationBuilder.DropTable(
                name: "svc_SalePayment");

            migrationBuilder.DropTable(
                name: "svc_SaleReceiptNumberCounter");

            migrationBuilder.DropTable(
                name: "svc_Sale");

            migrationBuilder.DropIndex(
                name: "IX_svc_StockMovement_LegacyShardKey_LegacyBranchId_SaleId",
                table: "svc_StockMovement");

            migrationBuilder.DropColumn(
                name: "SaleId",
                table: "svc_StockMovement");
        }
    }
}
