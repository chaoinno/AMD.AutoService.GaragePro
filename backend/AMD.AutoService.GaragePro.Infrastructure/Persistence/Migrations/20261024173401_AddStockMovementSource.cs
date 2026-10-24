using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMovementSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceMovementId",
                table: "svc_StockMovement",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_StockMovement_LegacyShardKey_LegacyBranchId_SourceMovementId",
                table: "svc_StockMovement",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "SourceMovementId" },
                filter: "[SourceMovementId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_svc_StockMovement_LegacyShardKey_LegacyBranchId_SourceMovementId",
                table: "svc_StockMovement");

            migrationBuilder.DropColumn(
                name: "SourceMovementId",
                table: "svc_StockMovement");
        }
    }
}
