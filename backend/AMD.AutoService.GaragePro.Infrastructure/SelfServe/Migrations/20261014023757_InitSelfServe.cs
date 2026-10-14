using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe.Migrations
{
    /// <inheritdoc />
    public partial class InitSelfServe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sp_Account",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    GoogleSubject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PasswordHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EmailVerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LoginUserName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SetupTokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SetupTokenExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedUserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sp_Account", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sp_BlockedDomain",
                columns: table => new
                {
                    Domain = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sp_BlockedDomain", x => x.Domain);
                });

            migrationBuilder.CreateTable(
                name: "sp_EmailOtp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    SentCount = table.Column<int>(type: "int", nullable: false),
                    LastSentAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WindowStartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sp_EmailOtp", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sp_JobRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RunDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    InstanceName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sp_JobRun", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sp_SignupAttempt",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Step = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EmailDomain = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sp_SignupAttempt", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sp_Workspace",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceNo = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1001, 1"),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProvinceId = table.Column<int>(type: "int", nullable: true),
                    Size = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Plan = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProvisionStep = table.Column<int>(type: "int", nullable: false),
                    ProvisionError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SignupRequestId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TrialStartsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TrialEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OwnerAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SampleDataLoadedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SampleDataError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DuplicatePhoneFlag = table.Column<bool>(type: "bit", nullable: false),
                    MobileLoginSeenAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExtensionRequestedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConvertedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConvertedToShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ConvertedToBranchId = table.Column<int>(type: "int", nullable: true),
                    PurgeAfter = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PurgedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UtmSource = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UtmMedium = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UtmCampaign = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sp_Workspace", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sp_Workspace_sp_Account_OwnerAccountId",
                        column: x => x.OwnerAccountId,
                        principalTable: "sp_Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sp_ConversionMap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TargetShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TargetBranchId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sp_ConversionMap", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sp_ConversionMap_sp_Workspace_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "sp_Workspace",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sp_SampleEntity",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sp_SampleEntity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sp_SampleEntity_sp_Workspace_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "sp_Workspace",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sp_WorkspaceAudit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sp_WorkspaceAudit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sp_WorkspaceAudit_sp_Workspace_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "sp_Workspace",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sp_WorkspaceNotice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sp_WorkspaceNotice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sp_WorkspaceNotice_sp_Workspace_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "sp_Workspace",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sp_Account_UserId",
                table: "sp_Account",
                column: "UserId",
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_sp_Account_GoogleSubject",
                table: "sp_Account",
                column: "GoogleSubject",
                unique: true,
                filter: "[GoogleSubject] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_sp_Account_LoginUserName",
                table: "sp_Account",
                column: "LoginUserName",
                unique: true,
                filter: "[LoginUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_sp_Account_NormalizedEmail",
                table: "sp_Account",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_sp_Account_SetupTokenHash",
                table: "sp_Account",
                column: "SetupTokenHash",
                unique: true,
                filter: "[SetupTokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_sp_ConversionMap_Workspace_Source",
                table: "sp_ConversionMap",
                columns: new[] { "WorkspaceId", "EntityType", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_sp_EmailOtp_Email",
                table: "sp_EmailOtp",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_sp_JobRun_JobName_RunDate",
                table: "sp_JobRun",
                columns: new[] { "JobName", "RunDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_sp_SampleEntity_Workspace_Entity",
                table: "sp_SampleEntity",
                columns: new[] { "WorkspaceId", "EntityType", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sp_SignupAttempt_CreatedAt",
                table: "sp_SignupAttempt",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_sp_SignupAttempt_Ip_CreatedAt",
                table: "sp_SignupAttempt",
                columns: new[] { "Ip", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sp_Workspace_Phone",
                table: "sp_Workspace",
                column: "Phone",
                filter: "[Phone] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sp_Workspace_Status_TrialEndsAt",
                table: "sp_Workspace",
                columns: new[] { "Status", "TrialEndsAt" });

            migrationBuilder.CreateIndex(
                name: "UX_sp_Workspace_BranchId",
                table: "sp_Workspace",
                column: "BranchId",
                unique: true,
                filter: "[BranchId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_sp_Workspace_Owner_SignupRequest",
                table: "sp_Workspace",
                columns: new[] { "OwnerAccountId", "SignupRequestId" },
                unique: true,
                filter: "[OwnerAccountId] IS NOT NULL AND [SignupRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_sp_Workspace_WorkspaceNo",
                table: "sp_Workspace",
                column: "WorkspaceNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sp_WorkspaceAudit_Workspace_CreatedAt",
                table: "sp_WorkspaceAudit",
                columns: new[] { "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_sp_WorkspaceNotice_Workspace_Kind",
                table: "sp_WorkspaceNotice",
                columns: new[] { "WorkspaceId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sp_BlockedDomain");

            migrationBuilder.DropTable(
                name: "sp_ConversionMap");

            migrationBuilder.DropTable(
                name: "sp_EmailOtp");

            migrationBuilder.DropTable(
                name: "sp_JobRun");

            migrationBuilder.DropTable(
                name: "sp_SampleEntity");

            migrationBuilder.DropTable(
                name: "sp_SignupAttempt");

            migrationBuilder.DropTable(
                name: "sp_WorkspaceAudit");

            migrationBuilder.DropTable(
                name: "sp_WorkspaceNotice");

            migrationBuilder.DropTable(
                name: "sp_Workspace");

            migrationBuilder.DropTable(
                name: "sp_Account");
        }
    }
}
