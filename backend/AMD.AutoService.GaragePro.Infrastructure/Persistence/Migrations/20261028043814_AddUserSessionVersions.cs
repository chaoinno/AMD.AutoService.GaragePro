using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSessionVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "svc_UserSessionVersion",
                columns: table => new
                {
                    ShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Changing = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_UserSessionVersion", x => new { x.ShardKey, x.UserId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ลบตารางนี้ = token รุ่น 0 ที่ถูกเพิกถอนแล้วกลับมาใช้ได้ — แก้ไปข้างหน้าแทนการย้อน (docs/19)
            throw new NotSupportedException("Session revocation data must be retained. This migration cannot be downgraded.");
        }
    }
}
