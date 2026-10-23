using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe.Migrations
{
    /// <inheritdoc />
    public partial class AddPackagePayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sp_PackagePayment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MonthCount = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SlipPath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SlipSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    SlipContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SlipSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SubmittedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    SubmittedByUserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PrevEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PeriodStartsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PeriodEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sp_PackagePayment", x => x.Id);
                    table.CheckConstraint("CK_sp_PackagePayment_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_sp_PackagePayment_MonthCount", "[MonthCount] > 0");
                    table.ForeignKey(
                        name: "FK_sp_PackagePayment_sp_Workspace_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "sp_Workspace",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sp_PackagePayment_SlipSha256",
                table: "sp_PackagePayment",
                column: "SlipSha256");

            migrationBuilder.CreateIndex(
                name: "IX_sp_PackagePayment_Status_SubmittedAt",
                table: "sp_PackagePayment",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_sp_PackagePayment_OnePending",
                table: "sp_PackagePayment",
                column: "WorkspaceId",
                unique: true,
                filter: "[Status] = N'Pending'");

            migrationBuilder.CreateIndex(
                name: "UX_sp_PackagePayment_Workspace_Request",
                table: "sp_PackagePayment",
                columns: new[] { "WorkspaceId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sp_PackagePayment");
        }
    }
}
