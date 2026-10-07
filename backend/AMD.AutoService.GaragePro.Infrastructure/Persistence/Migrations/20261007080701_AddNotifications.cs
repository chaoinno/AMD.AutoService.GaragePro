using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "svc_Notification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TitleTh = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BodyTh = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecipientStaffId = table.Column<long>(type: "bigint", nullable: true),
                    AudienceRoles = table.Column<int>(type: "int", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntityType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LinkHint = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ActorUserId = table.Column<long>(type: "bigint", nullable: false),
                    ActorStaffId = table.Column<long>(type: "bigint", nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SubjectKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_Notification", x => x.Id);
                    table.CheckConstraint("CK_svc_Notification_Recipient", "([RecipientStaffId] IS NOT NULL AND [AudienceRoles] = 0) OR ([RecipientStaffId] IS NULL AND [AudienceRoles] <> 0)");
                });

            migrationBuilder.CreateTable(
                name: "svc_NotificationRead",
                columns: table => new
                {
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffId = table.Column<long>(type: "bigint", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_NotificationRead", x => new { x.NotificationId, x.StaffId });
                    table.ForeignKey(
                        name: "FK_svc_NotificationRead_svc_Notification_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "svc_Notification",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_svc_Notification_Audience",
                table: "svc_Notification",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "CreatedAt" },
                filter: "[AudienceRoles] <> 0");

            migrationBuilder.CreateIndex(
                name: "IX_svc_Notification_OpenSubject",
                table: "svc_Notification",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "SubjectKey" },
                filter: "[SubjectKey] IS NOT NULL AND [ResolvedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_svc_Notification_Recipient",
                table: "svc_Notification",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "RecipientStaffId", "CreatedAt" },
                filter: "[RecipientStaffId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_NotificationRead");

            migrationBuilder.DropTable(
                name: "svc_Notification");
        }
    }
}
