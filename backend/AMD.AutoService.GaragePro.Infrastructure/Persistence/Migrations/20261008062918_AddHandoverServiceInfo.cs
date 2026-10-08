using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHandoverServiceInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MileageAtHandover",
                table: "svc_HandoverRecord",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "NextServiceDueOn",
                table: "svc_HandoverRecord",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NextServiceMileage",
                table: "svc_HandoverRecord",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NextServiceMonths",
                table: "svc_HandoverRecord",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ServiceInfoUpdatedAt",
                table: "svc_HandoverRecord",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceInfoUpdatedByUserName",
                table: "svc_HandoverRecord",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_HandoverRecord_NextServiceDueOn",
                table: "svc_HandoverRecord",
                column: "NextServiceDueOn",
                filter: "[NextServiceDueOn] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_svc_HandoverRecord_NextServiceDueOn",
                table: "svc_HandoverRecord");

            migrationBuilder.DropColumn(
                name: "MileageAtHandover",
                table: "svc_HandoverRecord");

            migrationBuilder.DropColumn(
                name: "NextServiceDueOn",
                table: "svc_HandoverRecord");

            migrationBuilder.DropColumn(
                name: "NextServiceMileage",
                table: "svc_HandoverRecord");

            migrationBuilder.DropColumn(
                name: "NextServiceMonths",
                table: "svc_HandoverRecord");

            migrationBuilder.DropColumn(
                name: "ServiceInfoUpdatedAt",
                table: "svc_HandoverRecord");

            migrationBuilder.DropColumn(
                name: "ServiceInfoUpdatedByUserName",
                table: "svc_HandoverRecord");
        }
    }
}
