using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "svc_JobChatMessage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ReplyToMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_JobChatMessage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_svc_JobChatMessage_svc_JobChatMessage_ReplyToMessageId",
                        column: x => x.ReplyToMessageId,
                        principalTable: "svc_JobChatMessage",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_svc_JobChatMessage_svc_Job_JobId",
                        column: x => x.JobId,
                        principalTable: "svc_Job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "svc_JobChatMention",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffId = table.Column<long>(type: "bigint", nullable: false),
                    StaffName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_JobChatMention", x => x.Id);
                    table.ForeignKey(
                        name: "FK_svc_JobChatMention_svc_JobChatMessage_MessageId",
                        column: x => x.MessageId,
                        principalTable: "svc_JobChatMessage",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_svc_JobChatMention_MessageId",
                table: "svc_JobChatMention",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_JobChatMention_StaffId",
                table: "svc_JobChatMention",
                column: "StaffId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_JobChatMessage_JobId_CreatedAt",
                table: "svc_JobChatMessage",
                columns: new[] { "JobId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_JobChatMessage_ReplyToMessageId",
                table: "svc_JobChatMessage",
                column: "ReplyToMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_JobChatMention");

            migrationBuilder.DropTable(
                name: "svc_JobChatMessage");
        }
    }
}
