using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.AppUpdateMigrations
{
    /// <inheritdoc />
    public partial class InitialAppUpdatePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "svc_AppUpdateAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    EditedByShard = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EditedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    EditedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EditedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_AppUpdateAudit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "svc_AppUpdatePolicy",
                columns: table => new
                {
                    Platform = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    LatestVersion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MinimumVersion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_AppUpdatePolicy", x => x.Platform);
                });

            migrationBuilder.CreateIndex(
                name: "IX_svc_AppUpdateAudit_Platform_Revision",
                table: "svc_AppUpdateAudit",
                columns: new[] { "Platform", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_AppUpdateAudit");

            migrationBuilder.DropTable(
                name: "svc_AppUpdatePolicy");
        }
    }
}
