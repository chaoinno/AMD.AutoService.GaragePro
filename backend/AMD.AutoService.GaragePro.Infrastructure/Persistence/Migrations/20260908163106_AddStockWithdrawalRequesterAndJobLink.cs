using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockWithdrawalRequesterAndJobLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "JobId",
                table: "svc_StockMovement",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequesterName",
                table: "svc_StockMovement",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RequesterStaffId",
                table: "svc_StockMovement",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_StockMovement_LegacyShardKey_LegacyBranchId_JobId",
                table: "svc_StockMovement",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "JobId" },
                filter: "[JobId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_svc_StockMovement_LegacyShardKey_LegacyBranchId_JobId",
                table: "svc_StockMovement");

            migrationBuilder.DropColumn(
                name: "JobId",
                table: "svc_StockMovement");

            migrationBuilder.DropColumn(
                name: "RequesterName",
                table: "svc_StockMovement");

            migrationBuilder.DropColumn(
                name: "RequesterStaffId",
                table: "svc_StockMovement");
        }
    }
}
