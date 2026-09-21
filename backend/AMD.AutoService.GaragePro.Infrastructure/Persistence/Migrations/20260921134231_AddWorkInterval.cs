using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkInterval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "svc_WorkInterval",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepairTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TechnicianStaffId = table.Column<long>(type: "bigint", nullable: false),
                    TechnicianName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndReason = table.Column<int>(type: "int", nullable: true),
                    ShiftSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsRework = table.Column<bool>(type: "bit", nullable: false),
                    IsAutoCapped = table.Column<bool>(type: "bit", nullable: false),
                    ClosedPreviousIntervalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    EndedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    EditedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    EditedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EditReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_WorkInterval", x => x.Id);
                    table.CheckConstraint("CK_WorkInterval_Range", "[EndedAt] IS NULL OR [EndedAt] >= [StartedAt]");
                    table.ForeignKey(
                        name: "FK_svc_WorkInterval_svc_Job_JobId",
                        column: x => x.JobId,
                        principalTable: "svc_Job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_svc_WorkInterval_JobId_StartedAt",
                table: "svc_WorkInterval",
                columns: new[] { "JobId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_WorkInterval_LegacyShardKey_LegacyBranchId_TechnicianStaffId_StartedAt",
                table: "svc_WorkInterval",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "TechnicianStaffId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_WorkInterval_RequestId",
                table: "svc_WorkInterval",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_WorkInterval_OneOpenPerTech",
                table: "svc_WorkInterval",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "TechnicianStaffId" },
                unique: true,
                filter: "[EndedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_WorkInterval");
        }
    }
}
