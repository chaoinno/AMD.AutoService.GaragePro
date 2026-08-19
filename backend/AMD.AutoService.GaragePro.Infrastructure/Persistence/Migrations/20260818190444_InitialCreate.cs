using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "svc_ActivityEvent",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    LegacyJobId = table.Column<long>(type: "bigint", nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntityType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DescriptionTh = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PerformedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    PerformedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_ActivityEvent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "svc_Attachment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    LegacyJobId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UploadedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_Attachment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "svc_CatalogItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Compatibility = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StandardHours = table.Column<decimal>(type: "decimal(6,2)", nullable: true),
                    OnHand = table.Column<int>(type: "int", nullable: false),
                    Reserved = table.Column<int>(type: "int", nullable: false),
                    OnOrder = table.Column<int>(type: "int", nullable: false),
                    Damaged = table.Column<int>(type: "int", nullable: false),
                    EtaNote = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_CatalogItem", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "svc_Quotation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    LegacyJobId = table.Column<long>(type: "bigint", nullable: false),
                    JobNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CustomerPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CustomerTaxId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CustomerAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VehicleRegistration = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    VehicleModel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VehicleVin = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    VehicleMileage = table.Column<int>(type: "int", nullable: true),
                    BranchName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BranchAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BranchTaxId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BranchPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineDiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PromotionAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DepositAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SupersedesQuotationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupersededByQuotationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevisionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LockedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    LockedByUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LockedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValidUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUpdatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_Quotation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "svc_Shift",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    SupervisorStaffId = table.Column<long>(type: "bigint", nullable: true),
                    SupervisorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_Shift", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "svc_ShiftSession",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    LegacyUserId = table.Column<long>(type: "bigint", nullable: false),
                    LegacyStaffId = table.Column<long>(type: "bigint", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShiftName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BranchName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    OpenedFrom = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_ShiftSession", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "svc_UserRoleOverride",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    LegacyUserId = table.Column<long>(type: "bigint", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SetByUserId = table.Column<long>(type: "bigint", nullable: false),
                    SetAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_UserRoleOverride", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "svc_QuotationApproval",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuotationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuotationVersion = table.Column<int>(type: "int", nullable: false),
                    SignatureImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsentText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    DeviceInfo = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    WitnessEmployeeId = table.Column<long>(type: "bigint", nullable: false),
                    WitnessEmployeeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApprovedLineCount = table.Column<int>(type: "int", nullable: false),
                    RejectedLineCount = table.Column<int>(type: "int", nullable: false),
                    ApprovedNetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_QuotationApproval", x => x.Id);
                    table.ForeignKey(
                        name: "FK_svc_QuotationApproval_svc_Quotation_QuotationId",
                        column: x => x.QuotationId,
                        principalTable: "svc_Quotation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "svc_QuotationLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuotationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    CatalogCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    InspectionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Promotion = table.Column<int>(type: "int", nullable: false),
                    AssignedTechnicianId = table.Column<long>(type: "bigint", nullable: true),
                    AssignedTechnicianName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StandardHours = table.Column<decimal>(type: "decimal(6,2)", nullable: true),
                    ApprovalStatus = table.Column<int>(type: "int", nullable: false),
                    RejectReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PromotionAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CostAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MarginAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_QuotationLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_svc_QuotationLine_svc_Quotation_QuotationId",
                        column: x => x.QuotationId,
                        principalTable: "svc_Quotation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_svc_ActivityEvent_EntityId",
                table: "svc_ActivityEvent",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_ActivityEvent_LegacyShardKey_LegacyBranchId_LegacyJobId_OccurredAt",
                table: "svc_ActivityEvent",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "LegacyJobId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_Attachment_LegacyShardKey_LegacyBranchId_LegacyJobId",
                table: "svc_Attachment",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "LegacyJobId" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_Attachment_RelativePath",
                table: "svc_Attachment",
                column: "RelativePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_CatalogItem_LegacyShardKey_LegacyBranchId_Code",
                table: "svc_CatalogItem",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_Quotation_Code",
                table: "svc_Quotation",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_svc_Quotation_LegacyShardKey_LegacyBranchId_LegacyJobId",
                table: "svc_Quotation",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "LegacyJobId" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_Quotation_LegacyShardKey_LegacyBranchId_Status",
                table: "svc_Quotation",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_QuotationApproval_QuotationId",
                table: "svc_QuotationApproval",
                column: "QuotationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_QuotationLine_QuotationId",
                table: "svc_QuotationLine",
                column: "QuotationId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_Shift_LegacyShardKey_LegacyBranchId_SortOrder",
                table: "svc_Shift",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_ShiftSession_LegacyShardKey_LegacyUserId_ClosedAt",
                table: "svc_ShiftSession",
                columns: new[] { "LegacyShardKey", "LegacyUserId", "ClosedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_UserRoleOverride_LegacyShardKey_LegacyUserId",
                table: "svc_UserRoleOverride",
                columns: new[] { "LegacyShardKey", "LegacyUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_ActivityEvent");

            migrationBuilder.DropTable(
                name: "svc_Attachment");

            migrationBuilder.DropTable(
                name: "svc_CatalogItem");

            migrationBuilder.DropTable(
                name: "svc_QuotationApproval");

            migrationBuilder.DropTable(
                name: "svc_QuotationLine");

            migrationBuilder.DropTable(
                name: "svc_Shift");

            migrationBuilder.DropTable(
                name: "svc_ShiftSession");

            migrationBuilder.DropTable(
                name: "svc_UserRoleOverride");

            migrationBuilder.DropTable(
                name: "svc_Quotation");
        }
    }
}
