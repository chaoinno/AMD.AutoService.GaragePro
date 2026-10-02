using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "svc_TaxInvoice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SellerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SellerAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SellerTaxId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SellerPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    BuyerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BuyerAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BuyerTaxId = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: true),
                    BuyerBranchNo = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    VatRate = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IssuedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    IssuedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_TaxInvoice", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "svc_TaxInvoiceNumberCounter",
                columns: table => new
                {
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastSequence = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_TaxInvoiceNumberCounter", x => new { x.LegacyShardKey, x.LegacyBranchId, x.Year });
                });

            migrationBuilder.CreateTable(
                name: "svc_TaxInvoiceLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    QuotationCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_TaxInvoiceLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_svc_TaxInvoiceLine_svc_TaxInvoice_TaxInvoiceId",
                        column: x => x.TaxInvoiceId,
                        principalTable: "svc_TaxInvoice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_svc_TaxInvoice_JobId",
                table: "svc_TaxInvoice",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_TaxInvoice_LegacyShardKey_LegacyBranchId_DocumentNo",
                table: "svc_TaxInvoice",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "DocumentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_TaxInvoice_ReceiptId",
                table: "svc_TaxInvoice",
                column: "ReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_TaxInvoiceLine_TaxInvoiceId_Sequence",
                table: "svc_TaxInvoiceLine",
                columns: new[] { "TaxInvoiceId", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_TaxInvoiceLine");

            migrationBuilder.DropTable(
                name: "svc_TaxInvoiceNumberCounter");

            migrationBuilder.DropTable(
                name: "svc_TaxInvoice");
        }
    }
}
