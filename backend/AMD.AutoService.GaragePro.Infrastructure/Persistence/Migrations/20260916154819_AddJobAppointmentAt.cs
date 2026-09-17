using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobAppointmentAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AppointmentAt",
                table: "svc_Job",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_Job_LegacyShardKey_BranchId_AppointmentAt",
                table: "svc_Job",
                columns: new[] { "LegacyShardKey", "BranchId", "AppointmentAt" },
                filter: "[AppointmentAt] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_svc_Job_LegacyShardKey_BranchId_AppointmentAt",
                table: "svc_Job");

            migrationBuilder.DropColumn(
                name: "AppointmentAt",
                table: "svc_Job");
        }
    }
}
