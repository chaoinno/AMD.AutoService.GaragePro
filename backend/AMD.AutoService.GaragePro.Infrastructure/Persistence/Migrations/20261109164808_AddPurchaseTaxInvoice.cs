using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseTaxInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "svc_PurchaseTaxInvoice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SupplierTaxId = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    SupplierBranchNo = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    InvoiceNo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ClaimPeriod = table.Column<int>(type: "int", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchaseOrderNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    GoodsReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GoodsReceiptNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RecordedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    RecordedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_PurchaseTaxInvoice", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_svc_PurchaseTaxInvoice_GoodsReceiptId",
                table: "svc_PurchaseTaxInvoice",
                column: "GoodsReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_PurchaseTaxInvoice_LegacyShardKey_LegacyBranchId_ClaimPeriod",
                table: "svc_PurchaseTaxInvoice",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "ClaimPeriod" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_PurchaseTaxInvoice_LegacyShardKey_LegacyBranchId_SupplierTaxId_InvoiceNo",
                table: "svc_PurchaseTaxInvoice",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "SupplierTaxId", "InvoiceNo" },
                unique: true,
                filter: "[VoidedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_svc_PurchaseTaxInvoice_RequestId",
                table: "svc_PurchaseTaxInvoice",
                column: "RequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_PurchaseTaxInvoice");
        }
    }
}
