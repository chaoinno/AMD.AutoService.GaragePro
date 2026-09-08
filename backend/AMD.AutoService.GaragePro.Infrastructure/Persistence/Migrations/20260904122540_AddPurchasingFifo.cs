using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchasingFifo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PurchasingLocked",
                table: "svc_CatalogItem",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "svc_CatalogItem",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<bool>(
                name: "StockManaged",
                table: "svc_CatalogItem",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "svc_PurchaseDocument",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    SourceRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequiredDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PaymentTerms = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApprovedBy = table.Column<long>(type: "bigint", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_PurchaseDocument", x => x.Id);
                    table.ForeignKey(
                        name: "FK_svc_PurchaseDocument_svc_PurchaseDocument_SourceRequestId",
                        column: x => x.SourceRequestId,
                        principalTable: "svc_PurchaseDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_svc_PurchaseDocument_svc_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "svc_Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_svc_PurchaseDocument_svc_Warehouse_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "svc_Warehouse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "svc_PurchaseNumberCounter",
                columns: table => new
                {
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_PurchaseNumberCounter", x => new { x.LegacyShardKey, x.LegacyBranchId, x.Kind, x.Date });
                });

            migrationBuilder.CreateTable(
                name: "svc_GoodsReceipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DeliveryNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_GoodsReceipt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_svc_GoodsReceipt_svc_PurchaseDocument_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "svc_PurchaseDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "svc_PurchaseLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReceivedGood = table.Column<int>(type: "int", nullable: false),
                    ReceivedDamaged = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_PurchaseLine", x => x.Id);
                    table.CheckConstraint("CK_PurchaseLine_Quantity", "[Quantity] > 0 AND [ReceivedGood] >= 0 AND [ReceivedDamaged] >= 0 AND [ReceivedGood] + [ReceivedDamaged] <= [Quantity] AND [UnitCost] >= 0");
                    table.ForeignKey(
                        name: "FK_svc_PurchaseLine_svc_CatalogItem_CatalogItemId",
                        column: x => x.CatalogItemId,
                        principalTable: "svc_CatalogItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_svc_PurchaseLine_svc_PurchaseDocument_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "svc_PurchaseDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "svc_GoodsReceiptLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoodsReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoodQuantity = table.Column<int>(type: "int", nullable: false),
                    DamagedQuantity = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_GoodsReceiptLine", x => x.Id);
                    table.CheckConstraint("CK_GoodsReceiptLine_Quantity", "[GoodQuantity] >= 0 AND [DamagedQuantity] >= 0 AND [GoodQuantity] + [DamagedQuantity] > 0 AND [UnitCost] >= 0");
                    table.ForeignKey(
                        name: "FK_svc_GoodsReceiptLine_svc_GoodsReceipt_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalTable: "svc_GoodsReceipt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_svc_GoodsReceiptLine_svc_PurchaseLine_PurchaseLineId",
                        column: x => x.PurchaseLineId,
                        principalTable: "svc_PurchaseLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "svc_StockLot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoodsReceiptLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedQuantity = table.Column<int>(type: "int", nullable: false),
                    RemainingQuantity = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_StockLot", x => x.Id);
                    table.CheckConstraint("CK_StockLot_Quantity", "[ReceivedQuantity] > 0 AND [RemainingQuantity] >= 0 AND [RemainingQuantity] <= [ReceivedQuantity] AND [UnitCost] >= 0");
                    table.ForeignKey(
                        name: "FK_svc_StockLot_svc_CatalogItem_CatalogItemId",
                        column: x => x.CatalogItemId,
                        principalTable: "svc_CatalogItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_svc_StockLot_svc_GoodsReceiptLine_GoodsReceiptLineId",
                        column: x => x.GoodsReceiptLineId,
                        principalTable: "svc_GoodsReceiptLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_svc_StockLot_svc_Warehouse_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "svc_Warehouse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "svc_StockMovement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyShardKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyBranchId = table.Column<int>(type: "int", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockLotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    DamagedQuantity = table.Column<int>(type: "int", nullable: false),
                    BalanceBefore = table.Column<int>(type: "int", nullable: false),
                    BalanceAfter = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PerformedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_StockMovement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_svc_StockMovement_svc_CatalogItem_CatalogItemId",
                        column: x => x.CatalogItemId,
                        principalTable: "svc_CatalogItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_svc_StockMovement_svc_StockLot_StockLotId",
                        column: x => x.StockLotId,
                        principalTable: "svc_StockLot",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_svc_StockMovement_svc_Warehouse_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "svc_Warehouse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_svc_GoodsReceipt_LegacyShardKey_LegacyBranchId_Number",
                table: "svc_GoodsReceipt",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_GoodsReceipt_LegacyShardKey_LegacyBranchId_RequestId",
                table: "svc_GoodsReceipt",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_GoodsReceipt_PurchaseOrderId",
                table: "svc_GoodsReceipt",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_GoodsReceiptLine_GoodsReceiptId_PurchaseLineId",
                table: "svc_GoodsReceiptLine",
                columns: new[] { "GoodsReceiptId", "PurchaseLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_GoodsReceiptLine_PurchaseLineId",
                table: "svc_GoodsReceiptLine",
                column: "PurchaseLineId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_PurchaseDocument_LegacyShardKey_LegacyBranchId_Kind_Status_CreatedAt",
                table: "svc_PurchaseDocument",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "Kind", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_PurchaseDocument_LegacyShardKey_LegacyBranchId_Number",
                table: "svc_PurchaseDocument",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_PurchaseDocument_SourceRequestId",
                table: "svc_PurchaseDocument",
                column: "SourceRequestId",
                unique: true,
                filter: "[SourceRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_svc_PurchaseDocument_SupplierId",
                table: "svc_PurchaseDocument",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_PurchaseDocument_WarehouseId",
                table: "svc_PurchaseDocument",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_PurchaseLine_CatalogItemId",
                table: "svc_PurchaseLine",
                column: "CatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_PurchaseLine_DocumentId_CatalogItemId",
                table: "svc_PurchaseLine",
                columns: new[] { "DocumentId", "CatalogItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_svc_StockLot_CatalogItemId",
                table: "svc_StockLot",
                column: "CatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_StockLot_GoodsReceiptLineId",
                table: "svc_StockLot",
                column: "GoodsReceiptLineId",
                unique: true,
                filter: "[GoodsReceiptLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_svc_StockLot_LegacyShardKey_LegacyBranchId_CatalogItemId_WarehouseId_ReceivedAt",
                table: "svc_StockLot",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "CatalogItemId", "WarehouseId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_StockLot_WarehouseId",
                table: "svc_StockLot",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_StockMovement_CatalogItemId",
                table: "svc_StockMovement",
                column: "CatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_StockMovement_LegacyShardKey_LegacyBranchId_CatalogItemId_OccurredAt",
                table: "svc_StockMovement",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "CatalogItemId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_StockMovement_LegacyShardKey_LegacyBranchId_OperationId",
                table: "svc_StockMovement",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "OperationId" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_StockMovement_LegacyShardKey_LegacyBranchId_OperationId_StockLotId",
                table: "svc_StockMovement",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "OperationId", "StockLotId" },
                unique: true,
                filter: "[StockLotId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_svc_StockMovement_StockLotId",
                table: "svc_StockMovement",
                column: "StockLotId");

            migrationBuilder.CreateIndex(
                name: "IX_svc_StockMovement_WarehouseId",
                table: "svc_StockMovement",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_PurchaseNumberCounter");

            migrationBuilder.DropTable(
                name: "svc_StockMovement");

            migrationBuilder.DropTable(
                name: "svc_StockLot");

            migrationBuilder.DropTable(
                name: "svc_GoodsReceiptLine");

            migrationBuilder.DropTable(
                name: "svc_GoodsReceipt");

            migrationBuilder.DropTable(
                name: "svc_PurchaseLine");

            migrationBuilder.DropTable(
                name: "svc_PurchaseDocument");

            migrationBuilder.DropColumn(
                name: "PurchasingLocked",
                table: "svc_CatalogItem");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "svc_CatalogItem");

            migrationBuilder.DropColumn(
                name: "StockManaged",
                table: "svc_CatalogItem");
        }
    }
}
