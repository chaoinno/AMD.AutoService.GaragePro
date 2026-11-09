using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SellerBranchNo",
                table: "svc_TaxInvoice",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "svc_BranchTaxProfile",
                columns: table => new
                {
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    SellerBranchNo = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    UpdatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_BranchTaxProfile", x => new { x.LegacyShardKey, x.LegacyBranchId });
                });

            migrationBuilder.CreateTable(
                name: "svc_CreditNote",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    JobNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    VehicleRegistration = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceDocumentNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SourceIssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReasonCode = table.Column<int>(type: "int", nullable: false),
                    ReasonText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SellerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SellerAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SellerTaxId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SellerBranchNo = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    BuyerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BuyerAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BuyerTaxId = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: true),
                    BuyerBranchNo = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    OriginalNetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CorrectNetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiffNetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IssuedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    IssuedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_CreditNote", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "svc_CreditNoteNumberCounter",
                columns: table => new
                {
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastSequence = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_CreditNoteNumberCounter", x => new { x.LegacyShardKey, x.LegacyBranchId, x.Year });
                });

            migrationBuilder.CreateTable(
                name: "svc_CreditNoteLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    SourceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuotationCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OriginalQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    OriginalNetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_CreditNoteLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_svc_CreditNoteLine_svc_CreditNote_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalTable: "svc_CreditNote",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "svc_CreditNoteRefund",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefundedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    RefundedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RefundedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_CreditNoteRefund", x => x.Id);
                    table.ForeignKey(
                        name: "FK_svc_CreditNoteRefund_svc_CreditNote_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalTable: "svc_CreditNote",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_svc_CreditNote_JobId",
                table: "svc_CreditNote",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_CreditNote_LegacyShardKey_LegacyBranchId_DocumentNo",
                table: "svc_CreditNote",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "DocumentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_CreditNote_LegacyShardKey_LegacyBranchId_IssuedAt",
                table: "svc_CreditNote",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_CreditNote_RequestId",
                table: "svc_CreditNote",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_CreditNote_TaxInvoiceId",
                table: "svc_CreditNote",
                column: "TaxInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_CreditNoteLine_CreditNoteId",
                table: "svc_CreditNoteLine",
                column: "CreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_CreditNoteLine_SourceLineId",
                table: "svc_CreditNoteLine",
                column: "SourceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_CreditNoteRefund_CreditNoteId",
                table: "svc_CreditNoteRefund",
                column: "CreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_CreditNoteRefund_RequestId",
                table: "svc_CreditNoteRefund",
                column: "RequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_BranchTaxProfile");

            migrationBuilder.DropTable(
                name: "svc_CreditNoteLine");

            migrationBuilder.DropTable(
                name: "svc_CreditNoteNumberCounter");

            migrationBuilder.DropTable(
                name: "svc_CreditNoteRefund");

            migrationBuilder.DropTable(
                name: "svc_CreditNote");

            migrationBuilder.DropColumn(
                name: "SellerBranchNo",
                table: "svc_TaxInvoice");
        }
    }
}
