using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RebuildJobAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // [BIZ] ล้างข้อมูล transaction ที่ผูกกับ Job ทั้งหมดก่อนปรับ schema — จ๊อบไม่มี LegacyJobId
            // อีกต่อไป (ยกเลิกข้อยกเว้น 2026-08-26 เมื่อ 2026-08-31) รูปแบบคีย์เปลี่ยนจนไม่สามารถแปลงข้อมูลเดิมได้
            // ข้อมูลนี้เป็นข้อมูลพัฒนา/ทดสอบที่สร้างซ้ำได้ผ่าน tools/devseed ไม่ใช่ข้อมูลจริงของลูกค้า
            // เรียงลบจากตารางลูกไปตารางแม่ — svc_Shift/svc_ShiftSession/svc_UserRoleOverride/svc_CatalogItem ไม่แตะ
            migrationBuilder.Sql("DELETE FROM svc_ActivityEvent;");
            migrationBuilder.Sql("DELETE FROM svc_QuotationApproval;");
            migrationBuilder.Sql("DELETE FROM svc_QuotationLine;");
            migrationBuilder.Sql("DELETE FROM svc_Attachment;");
            migrationBuilder.Sql("DELETE FROM svc_IntakeChecklistItem;");
            migrationBuilder.Sql("DELETE FROM svc_IntakeChecklist;");
            migrationBuilder.Sql("DELETE FROM svc_Quotation;");
            migrationBuilder.Sql("DELETE FROM svc_Job;");

            migrationBuilder.DropIndex(
                name: "IX_svc_Quotation_LegacyShardKey_LegacyBranchId_LegacyJobId",
                table: "svc_Quotation");

            migrationBuilder.DropIndex(
                name: "IX_svc_Quotation_LegacyShardKey_LegacyBranchId_Status",
                table: "svc_Quotation");

            migrationBuilder.DropIndex(
                name: "IX_svc_Job_LegacyShardKey_LegacyBranchId_LegacyJobId",
                table: "svc_Job");

            migrationBuilder.DropIndex(
                name: "IX_svc_Job_LegacyShardKey_LegacyBranchId_Status",
                table: "svc_Job");

            migrationBuilder.DropIndex(
                name: "IX_svc_IntakeChecklist_LegacyShardKey_LegacyBranchId_LegacyJobId",
                table: "svc_IntakeChecklist");

            migrationBuilder.DropIndex(
                name: "IX_svc_Attachment_LegacyShardKey_LegacyBranchId_LegacyJobId",
                table: "svc_Attachment");

            migrationBuilder.DropIndex(
                name: "IX_svc_ActivityEvent_LegacyShardKey_LegacyBranchId_LegacyJobId_OccurredAt",
                table: "svc_ActivityEvent");

            migrationBuilder.DropColumn(
                name: "LegacyBranchId",
                table: "svc_Quotation");

            migrationBuilder.DropColumn(
                name: "LegacyJobId",
                table: "svc_Quotation");

            migrationBuilder.DropColumn(
                name: "LegacyShardKey",
                table: "svc_Quotation");

            migrationBuilder.DropColumn(
                name: "WasBackfilled",
                table: "svc_Job");

            migrationBuilder.DropColumn(
                name: "LegacyBranchId",
                table: "svc_IntakeChecklist");

            migrationBuilder.DropColumn(
                name: "LegacyJobId",
                table: "svc_IntakeChecklist");

            migrationBuilder.DropColumn(
                name: "LegacyShardKey",
                table: "svc_IntakeChecklist");

            migrationBuilder.DropColumn(
                name: "LegacyBranchId",
                table: "svc_Attachment");

            migrationBuilder.DropColumn(
                name: "LegacyJobId",
                table: "svc_Attachment");

            migrationBuilder.DropColumn(
                name: "LegacyShardKey",
                table: "svc_Attachment");

            migrationBuilder.DropColumn(
                name: "LegacyBranchId",
                table: "svc_ActivityEvent");

            migrationBuilder.DropColumn(
                name: "LegacyJobId",
                table: "svc_ActivityEvent");

            migrationBuilder.DropColumn(
                name: "LegacyShardKey",
                table: "svc_ActivityEvent");

            migrationBuilder.RenameColumn(
                name: "LegacyJobId",
                table: "svc_Job",
                newName: "VehicleId");

            migrationBuilder.RenameColumn(
                name: "LegacyBranchId",
                table: "svc_Job",
                newName: "JobTypeId");

            migrationBuilder.AddColumn<Guid>(
                name: "JobId",
                table: "svc_Quotation",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "svc_Job",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BranchName",
                table: "svc_Job",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "CustomerId",
                table: "svc_Job",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                table: "svc_Job",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CustomerPhone",
                table: "svc_Job",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Detail",
                table: "svc_Job",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JobTypeName",
                table: "svc_Job",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SenderName",
                table: "svc_Job",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SenderPhoneNumber",
                table: "svc_Job",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleImagePath",
                table: "svc_Job",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleModel",
                table: "svc_Job",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleRegistration",
                table: "svc_Job",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VehicleVin",
                table: "svc_Job",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "JobId",
                table: "svc_IntakeChecklist",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "JobId",
                table: "svc_Attachment",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "JobId",
                table: "svc_ActivityEvent",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "svc_JobNumberCounter",
                columns: table => new
                {
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    CounterDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LastSequence = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_JobNumberCounter", x => new { x.LegacyShardKey, x.BranchId, x.CounterDate });
                });

            migrationBuilder.CreateIndex(
                name: "IX_svc_Quotation_JobId",
                table: "svc_Quotation",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_Quotation_Status",
                table: "svc_Quotation",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_svc_Job_JobNo",
                table: "svc_Job",
                column: "JobNo");

            migrationBuilder.CreateIndex(
                name: "IX_svc_Job_LegacyShardKey_BranchId_Status",
                table: "svc_Job",
                columns: new[] { "LegacyShardKey", "BranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_Job_LegacyShardKey_BranchId_VehicleId_Status",
                table: "svc_Job",
                columns: new[] { "LegacyShardKey", "BranchId", "VehicleId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_IntakeChecklist_JobId",
                table: "svc_IntakeChecklist",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_Attachment_JobId",
                table: "svc_Attachment",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_ActivityEvent_JobId_OccurredAt",
                table: "svc_ActivityEvent",
                columns: new[] { "JobId", "OccurredAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_svc_ActivityEvent_svc_Job_JobId",
                table: "svc_ActivityEvent",
                column: "JobId",
                principalTable: "svc_Job",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_svc_Attachment_svc_Job_JobId",
                table: "svc_Attachment",
                column: "JobId",
                principalTable: "svc_Job",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_svc_IntakeChecklist_svc_Job_JobId",
                table: "svc_IntakeChecklist",
                column: "JobId",
                principalTable: "svc_Job",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_svc_Quotation_svc_Job_JobId",
                table: "svc_Quotation",
                column: "JobId",
                principalTable: "svc_Job",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <summary>
        /// [BIZ] ย้อนกลับได้เฉพาะ schema — ข้อมูลที่ Up() ลบไปด้วย DELETE ไม่สามารถกู้คืนได้
        /// (one-way data reset, ไม่ใช่ backup/restore)
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_svc_ActivityEvent_svc_Job_JobId",
                table: "svc_ActivityEvent");

            migrationBuilder.DropForeignKey(
                name: "FK_svc_Attachment_svc_Job_JobId",
                table: "svc_Attachment");

            migrationBuilder.DropForeignKey(
                name: "FK_svc_IntakeChecklist_svc_Job_JobId",
                table: "svc_IntakeChecklist");

            migrationBuilder.DropForeignKey(
                name: "FK_svc_Quotation_svc_Job_JobId",
                table: "svc_Quotation");

            migrationBuilder.DropTable(
                name: "svc_JobNumberCounter");

            migrationBuilder.DropIndex(
                name: "IX_svc_Quotation_JobId",
                table: "svc_Quotation");

            migrationBuilder.DropIndex(
                name: "IX_svc_Quotation_Status",
                table: "svc_Quotation");

            migrationBuilder.DropIndex(
                name: "IX_svc_Job_JobNo",
                table: "svc_Job");

            migrationBuilder.DropIndex(
                name: "IX_svc_Job_LegacyShardKey_BranchId_Status",
                table: "svc_Job");

            migrationBuilder.DropIndex(
                name: "IX_svc_Job_LegacyShardKey_BranchId_VehicleId_Status",
                table: "svc_Job");

            migrationBuilder.DropIndex(
                name: "IX_svc_IntakeChecklist_JobId",
                table: "svc_IntakeChecklist");

            migrationBuilder.DropIndex(
                name: "IX_svc_Attachment_JobId",
                table: "svc_Attachment");

            migrationBuilder.DropIndex(
                name: "IX_svc_ActivityEvent_JobId_OccurredAt",
                table: "svc_ActivityEvent");

            migrationBuilder.DropColumn(
                name: "JobId",
                table: "svc_Quotation");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "svc_Job");

            migrationBuilder.DropColumn(
                name: "BranchName",
                table: "svc_Job");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "svc_Job");

            migrationBuilder.DropColumn(
                name: "CustomerName",
                table: "svc_Job");

            migrationBuilder.DropColumn(
                name: "CustomerPhone",
                table: "svc_Job");

            migrationBuilder.DropColumn(
                name: "Detail",
                table: "svc_Job");

            migrationBuilder.DropColumn(
                name: "JobTypeName",
                table: "svc_Job");

            migrationBuilder.DropColumn(
                name: "SenderName",
                table: "svc_Job");

            migrationBuilder.DropColumn(
                name: "SenderPhoneNumber",
                table: "svc_Job");

            migrationBuilder.DropColumn(
                name: "VehicleImagePath",
                table: "svc_Job");

            migrationBuilder.DropColumn(
                name: "VehicleModel",
                table: "svc_Job");

            migrationBuilder.DropColumn(
                name: "VehicleRegistration",
                table: "svc_Job");

            migrationBuilder.DropColumn(
                name: "VehicleVin",
                table: "svc_Job");

            migrationBuilder.DropColumn(
                name: "JobId",
                table: "svc_IntakeChecklist");

            migrationBuilder.DropColumn(
                name: "JobId",
                table: "svc_Attachment");

            migrationBuilder.DropColumn(
                name: "JobId",
                table: "svc_ActivityEvent");

            migrationBuilder.RenameColumn(
                name: "VehicleId",
                table: "svc_Job",
                newName: "LegacyJobId");

            migrationBuilder.RenameColumn(
                name: "JobTypeId",
                table: "svc_Job",
                newName: "LegacyBranchId");

            migrationBuilder.AddColumn<int>(
                name: "LegacyBranchId",
                table: "svc_Quotation",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "LegacyJobId",
                table: "svc_Quotation",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "LegacyShardKey",
                table: "svc_Quotation",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "WasBackfilled",
                table: "svc_Job",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LegacyBranchId",
                table: "svc_IntakeChecklist",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "LegacyJobId",
                table: "svc_IntakeChecklist",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "LegacyShardKey",
                table: "svc_IntakeChecklist",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "LegacyBranchId",
                table: "svc_Attachment",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "LegacyJobId",
                table: "svc_Attachment",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "LegacyShardKey",
                table: "svc_Attachment",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "LegacyBranchId",
                table: "svc_ActivityEvent",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "LegacyJobId",
                table: "svc_ActivityEvent",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "LegacyShardKey",
                table: "svc_ActivityEvent",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_svc_Quotation_LegacyShardKey_LegacyBranchId_LegacyJobId",
                table: "svc_Quotation",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "LegacyJobId" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_Quotation_LegacyShardKey_LegacyBranchId_Status",
                table: "svc_Quotation",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_Job_LegacyShardKey_LegacyBranchId_LegacyJobId",
                table: "svc_Job",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "LegacyJobId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_Job_LegacyShardKey_LegacyBranchId_Status",
                table: "svc_Job",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_IntakeChecklist_LegacyShardKey_LegacyBranchId_LegacyJobId",
                table: "svc_IntakeChecklist",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "LegacyJobId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_Attachment_LegacyShardKey_LegacyBranchId_LegacyJobId",
                table: "svc_Attachment",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "LegacyJobId" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_ActivityEvent_LegacyShardKey_LegacyBranchId_LegacyJobId_OccurredAt",
                table: "svc_ActivityEvent",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "LegacyJobId", "OccurredAt" });
        }
    }
}
