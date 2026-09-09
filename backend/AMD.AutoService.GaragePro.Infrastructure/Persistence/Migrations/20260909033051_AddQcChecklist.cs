using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQcChecklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "svc_QcChecklist",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TestDriveKm = table.Column<decimal>(type: "decimal(8,1)", nullable: true),
                    TestDriveNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TestDriveRecordedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TestDriveRecordedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    TestDriveRecordedByUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    SubmittedByUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_QcChecklist", x => x.Id);
                    table.ForeignKey(
                        name: "FK_svc_QcChecklist_svc_Job_JobId",
                        column: x => x.JobId,
                        principalTable: "svc_Job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "svc_QcChecklistItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QcChecklistId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuotationLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CatalogCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Result = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedByUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_QcChecklistItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_svc_QcChecklistItem_svc_QcChecklist_QcChecklistId",
                        column: x => x.QcChecklistId,
                        principalTable: "svc_QcChecklist",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_svc_QcChecklist_JobId",
                table: "svc_QcChecklist",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_QcChecklistItem_QcChecklistId_QuotationLineId",
                table: "svc_QcChecklistItem",
                columns: new[] { "QcChecklistId", "QuotationLineId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_QcChecklistItem");

            migrationBuilder.DropTable(
                name: "svc_QcChecklist");
        }
    }
}
