using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHandoverServiceItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "svc_HandoverServiceItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HandoverRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NextServiceMileage = table.Column<int>(type: "int", nullable: false),
                    NextServiceMonths = table.Column<int>(type: "int", nullable: false),
                    NextServiceDueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_HandoverServiceItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_svc_HandoverServiceItem_svc_HandoverRecord_HandoverRecordId",
                        column: x => x.HandoverRecordId,
                        principalTable: "svc_HandoverRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "svc_ServiceReminderTemplate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DefaultMileageInterval = table.Column<int>(type: "int", nullable: false),
                    DefaultMonths = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_ServiceReminderTemplate", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_svc_HandoverServiceItem_NextServiceDueOn",
                table: "svc_HandoverServiceItem",
                column: "NextServiceDueOn");

            migrationBuilder.CreateIndex(
                name: "UX_svc_HandoverServiceItem_Name",
                table: "svc_HandoverServiceItem",
                columns: new[] { "HandoverRecordId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_svc_ServiceReminderTemplate_Name",
                table: "svc_ServiceReminderTemplate",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "Name" },
                unique: true);

            // [BIZ] ใบส่งมอบเดิมที่มีนัดครั้งถัดไป (คอลัมน์ NextService* เดี่ยว) → 1 รายการชื่อ "เช็คระยะ"
            // ให้รายงาน/ใบส่งมอบที่พิมพ์ซ้ำเห็นข้อมูลเดิมครบ · คอลัมน์เดิมคงไว้ ไม่ drop
            // บันทึกพรีวิวจะตั้งทั้งสามค่าพร้อมกันเสมอ จึงเช็คครบทั้งสามกัน NULL หลุดเข้าคอลัมน์ NOT NULL
            migrationBuilder.Sql(@"
INSERT INTO [svc_HandoverServiceItem]
    ([Id], [HandoverRecordId], [TemplateId], [Name], [NextServiceMileage], [NextServiceMonths], [NextServiceDueOn], [SortOrder])
SELECT NEWID(), h.[Id], NULL, N'เช็คระยะ', h.[NextServiceMileage], h.[NextServiceMonths], h.[NextServiceDueOn], 0
FROM [svc_HandoverRecord] h
WHERE h.[NextServiceMileage] IS NOT NULL
  AND h.[NextServiceMonths] IS NOT NULL
  AND h.[NextServiceDueOn] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [svc_HandoverServiceItem] i WHERE i.[HandoverRecordId] = h.[Id]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_HandoverServiceItem");

            migrationBuilder.DropTable(
                name: "svc_ServiceReminderTemplate");
        }
    }
}
