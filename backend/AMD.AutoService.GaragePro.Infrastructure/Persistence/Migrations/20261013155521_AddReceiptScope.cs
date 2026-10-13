using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// [BIZ] เลขใบเสร็จ RC-{yy}-{seq} นับต่อ (ชาร์ด, สาขา, ปี) ที่ svc_ReceiptNumberCounter แต่ svc_Receipt เคยมี unique
    /// ของ DocumentNo ทั้งตาราง (IX_svc_Receipt_DocumentNo) — สาขาที่สองออกใบแรก RC-26-0001 จึงชน (SqlException 2601 → 500)
    /// แก้โดยเพิ่ม LegacyShardKey/LegacyBranchId ให้ใบเสร็จ (เหมือน svc_Payment) แล้วย้าย unique ไปเป็น
    /// (ชาร์ด, สาขา, DocumentNo) ชื่อ UX_svc_Receipt_Scope_DocumentNo
    ///
    /// ปลอดภัยกับแถวเดิม: เพิ่มคอลัมน์แบบ nullable → backfill จาก svc_Job ของใบเสร็จนั้น (แหล่งเดียวที่เชื่อถือได้ —
    /// ใบเสร็จไม่เคยเก็บสาขาเอง) → ถ้ายังมีแถวที่หาจ๊อบไม่เจอให้หยุดพร้อมข้อความชัดเจนแทนที่จะเดาค่า → ค่อยบังคับ NOT NULL
    /// ไม่ใช้ defaultValue ของ AddColumn เพราะจะทิ้ง DEFAULT constraint ค้างไว้บนตารางและซ่อนแถวที่ backfill ไม่ได้
    /// </summary>
    public partial class AddReceiptScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LegacyShardKey",
                table: "svc_Receipt",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LegacyBranchId",
                table: "svc_Receipt",
                type: "int",
                nullable: true);

            // ใบเสร็จผูกกับจ๊อบเสมอ (IX_svc_Receipt_JobId unique) — สาขา/ชาร์ดของจ๊อบคือสาขา/ชาร์ดที่ออกใบเสร็จนั้น
            migrationBuilder.Sql("""
                UPDATE r SET r.LegacyShardKey = j.LegacyShardKey, r.LegacyBranchId = j.BranchId
                FROM svc_Receipt r JOIN svc_Job j ON j.Id = r.JobId;

                IF EXISTS (SELECT 1 FROM svc_Receipt WHERE LegacyShardKey IS NULL OR LegacyBranchId IS NULL)
                    THROW 50000, N'AddReceiptScope: svc_Receipt has rows without a matching svc_Job - set LegacyShardKey/LegacyBranchId for them before migrating.', 1;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "LegacyShardKey",
                table: "svc_Receipt",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "LegacyBranchId",
                table: "svc_Receipt",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_svc_Receipt_DocumentNo",
                table: "svc_Receipt");

            migrationBuilder.CreateIndex(
                name: "UX_svc_Receipt_Scope_DocumentNo",
                table: "svc_Receipt",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "DocumentNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_svc_Receipt_Scope_DocumentNo",
                table: "svc_Receipt");

            // คืน unique ทั้งตาราง — จะล้มถ้าระหว่างนี้มีหลายสาขาออกเลขเดียวกันไปแล้ว (ซึ่งคือสิ่งที่ migration นี้เปิดให้ทำ)
            // ต้องจัดการแถวที่ชนเองก่อนย้อน
            migrationBuilder.CreateIndex(
                name: "IX_svc_Receipt_DocumentNo",
                table: "svc_Receipt",
                column: "DocumentNo",
                unique: true);

            migrationBuilder.DropColumn(
                name: "LegacyBranchId",
                table: "svc_Receipt");

            migrationBuilder.DropColumn(
                name: "LegacyShardKey",
                table: "svc_Receipt");
        }
    }
}
