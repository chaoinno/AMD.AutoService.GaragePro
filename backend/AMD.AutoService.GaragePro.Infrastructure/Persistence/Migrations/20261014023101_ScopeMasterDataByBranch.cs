using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// [BIZ] ซัพพลายเออร์/หมวดหมู่สินค้า/คลัง เป็นข้อมูลของสาขา — เดิม svc_Supplier/svc_CatalogCategory ไม่มีคอลัมน์สาขาเลย
    /// และ unique ของ Code เป็นทั้งตาราง (ทุกอู่เห็น/แก้ของกันได้ และรหัสเดียวกันชนข้ามอู่) ส่วน svc_Warehouse มีสาขาแต่เป็น nullable
    /// แก้เป็น: ทั้งสามตารางมี (LegacyShardKey, LegacyBranchId) NOT NULL และ unique ย้ายเป็น (ชาร์ด, สาขา, Code)
    /// **ชื่อ index คงเดิม** (UX_svc_*_Code) เพราะ MasterDataSupport.ConflictCode และ MasterDataRepository.KnownConstraints จับจากชื่อ
    ///
    /// backfill จากของที่ผูกอยู่จริงเท่านั้น และหยุดทันทีถ้าแถวเดียวถูกใช้โดยมากกว่าหนึ่งสาขา (ไม่เดาว่าเป็นของใคร —
    /// ต้องแยกเป็นแถวละสาขาเองก่อน):
    /// · ซัพพลายเออร์ ← svc_PurchaseDocument.SupplierId + svc_CatalogItemSupplier (สาขาของสินค้า)
    /// · หมวดหมู่ ← svc_CatalogItem.CategoryId แล้วไล่ขึ้นหมวดแม่ทุกชั้น (หมวดแม่อยู่สาขาเดียวกับลูกทุกตัว) และหมวดย่อยที่ไม่มีสินค้า
    ///   รับสาขาของหมวดแม่ใกล้สุด — สุดท้ายตรวจว่าไม่มีคู่แม่–ลูกต่างสาขา
    /// · คลังที่ยังไม่มีสาขา ← svc_CatalogItem / svc_PurchaseDocument / svc_StockLot / svc_StockMovement / svc_Sale
    /// · ที่เหลือไม่มีอะไรผูก → 'db2'/227 — [ASSUME] ตรวจฐานจริง 2026-10-13 แล้ว: ซัพพลายเออร์ 5 (SUP-001..003 ผูก PO ของ db2/227,
    ///   SUP-004/005 ไม่ผูก) หมวดหมู่ 3 และคลัง 1 ของ db2/227 — ทุกแถวที่มีวันนี้เป็นของสาขาทดสอบ Service Center Demo
    ///
    /// เพิ่มคอลัมน์แบบ nullable → backfill → ค่อยบังคับ NOT NULL (ไม่ใช้ defaultValue ของ AddColumn เพราะจะทิ้ง DEFAULT
    /// constraint ค้างไว้และซ่อนแถวที่ backfill ไม่ได้ — แบบเดียวกับ AddReceiptScope)
    /// </summary>
    public partial class ScopeMasterDataByBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LegacyShardKey",
                table: "svc_Supplier",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LegacyBranchId",
                table: "svc_Supplier",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyShardKey",
                table: "svc_CatalogCategory",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LegacyBranchId",
                table: "svc_CatalogCategory",
                type: "int",
                nullable: true);

            // ---------- ซัพพลายเออร์ ----------
            migrationBuilder.Sql("""
                DECLARE @supplierRefs TABLE (Id uniqueidentifier NOT NULL, LegacyShardKey nvarchar(20) NOT NULL, LegacyBranchId int NOT NULL);

                INSERT INTO @supplierRefs (Id, LegacyShardKey, LegacyBranchId)
                SELECT d.SupplierId, d.LegacyShardKey, d.LegacyBranchId
                FROM svc_PurchaseDocument d WHERE d.SupplierId IS NOT NULL
                UNION
                SELECT l.SupplierId, i.LegacyShardKey, i.LegacyBranchId
                FROM svc_CatalogItemSupplier l JOIN svc_CatalogItem i ON i.Id = l.CatalogItemId;

                DECLARE @badSupplier nvarchar(30) = (
                    SELECT TOP 1 s.Code FROM svc_Supplier s
                    WHERE s.Id IN (SELECT Id FROM @supplierRefs GROUP BY Id HAVING COUNT(*) > 1)
                    ORDER BY s.Code);
                IF @badSupplier IS NOT NULL
                BEGIN
                    DECLARE @supplierMessage nvarchar(2048) = CONCAT(
                        N'ScopeMasterDataByBranch: supplier ', @badSupplier,
                        N' is used by more than one branch (svc_PurchaseDocument/svc_CatalogItemSupplier) - split it into one supplier row per branch before migrating.');
                    THROW 50000, @supplierMessage, 1;
                END;

                UPDATE s SET s.LegacyShardKey = r.LegacyShardKey, s.LegacyBranchId = r.LegacyBranchId
                FROM svc_Supplier s JOIN @supplierRefs r ON r.Id = s.Id;

                UPDATE svc_Supplier SET LegacyShardKey = N'db2', LegacyBranchId = 227
                WHERE LegacyShardKey IS NULL OR LegacyBranchId IS NULL;
                """);

            // ---------- หมวดหมู่ ----------
            migrationBuilder.Sql("""
                DECLARE @categoryRefs TABLE (Id uniqueidentifier NOT NULL, LegacyShardKey nvarchar(20) NOT NULL, LegacyBranchId int NOT NULL);

                -- สาขาของสินค้าที่ใช้หมวดนั้น แล้วไล่ขึ้นหมวดแม่ทุกชั้น (Depth กันวนไม่รู้จบถ้าข้อมูลเดิมมีวงจร)
                WITH up AS (
                    SELECT i.CategoryId AS Id, i.LegacyShardKey, i.LegacyBranchId, 0 AS Depth
                    FROM svc_CatalogItem i WHERE i.CategoryId IS NOT NULL
                    UNION ALL
                    SELECT c.ParentCategoryId, up.LegacyShardKey, up.LegacyBranchId, up.Depth + 1
                    FROM up JOIN svc_CatalogCategory c ON c.Id = up.Id
                    WHERE c.ParentCategoryId IS NOT NULL AND up.Depth < 50
                )
                INSERT INTO @categoryRefs (Id, LegacyShardKey, LegacyBranchId)
                SELECT DISTINCT Id, LegacyShardKey, LegacyBranchId FROM up;

                DECLARE @badCategory nvarchar(30) = (
                    SELECT TOP 1 c.Code FROM svc_CatalogCategory c
                    WHERE c.Id IN (SELECT Id FROM @categoryRefs GROUP BY Id HAVING COUNT(*) > 1)
                    ORDER BY c.Code);
                IF @badCategory IS NOT NULL
                BEGIN
                    DECLARE @categoryMessage nvarchar(2048) = CONCAT(
                        N'ScopeMasterDataByBranch: category ', @badCategory,
                        N' (or one of its sub-categories) is used by catalog items of more than one branch - split it into one category tree per branch before migrating.');
                    THROW 50000, @categoryMessage, 1;
                END;

                UPDATE c SET c.LegacyShardKey = r.LegacyShardKey, c.LegacyBranchId = r.LegacyBranchId
                FROM svc_CatalogCategory c JOIN @categoryRefs r ON r.Id = c.Id;

                -- หมวดย่อยที่ไม่มีสินค้ารับสาขาของหมวดแม่ใกล้สุด (ทีละชั้น)
                DECLARE @depth int = 0;
                WHILE @depth < 50
                BEGIN
                    UPDATE c SET c.LegacyShardKey = p.LegacyShardKey, c.LegacyBranchId = p.LegacyBranchId
                    FROM svc_CatalogCategory c JOIN svc_CatalogCategory p ON p.Id = c.ParentCategoryId
                    WHERE c.LegacyShardKey IS NULL AND p.LegacyShardKey IS NOT NULL;
                    IF @@ROWCOUNT = 0 BREAK;
                    SET @depth += 1;
                END;

                -- ทั้งต้นไม่มีสินค้าผูกเลย → ทั้งต้นไปสาขาเดียวกัน จึงไม่เกิดแม่–ลูกต่างสาขา
                UPDATE svc_CatalogCategory SET LegacyShardKey = N'db2', LegacyBranchId = 227
                WHERE LegacyShardKey IS NULL OR LegacyBranchId IS NULL;

                IF EXISTS (
                    SELECT 1 FROM svc_CatalogCategory c JOIN svc_CatalogCategory p ON p.Id = c.ParentCategoryId
                    WHERE c.LegacyShardKey <> p.LegacyShardKey OR c.LegacyBranchId <> p.LegacyBranchId)
                    THROW 50000, N'ScopeMasterDataByBranch: a sub-category ended up in a different branch from its parent category - fix svc_CatalogCategory.ParentCategoryId before migrating.', 1;
                """);

            // ---------- คลัง (เฉพาะแถวที่ยังไม่มีสาขา) ----------
            migrationBuilder.Sql("""
                DECLARE @warehouseRefs TABLE (Id uniqueidentifier NOT NULL, LegacyShardKey nvarchar(20) NOT NULL, LegacyBranchId int NOT NULL);

                INSERT INTO @warehouseRefs (Id, LegacyShardKey, LegacyBranchId)
                SELECT r.Id, r.LegacyShardKey, r.LegacyBranchId
                FROM (
                    SELECT i.WarehouseId AS Id, i.LegacyShardKey, i.LegacyBranchId FROM svc_CatalogItem i WHERE i.WarehouseId IS NOT NULL
                    UNION SELECT d.WarehouseId, d.LegacyShardKey, d.LegacyBranchId FROM svc_PurchaseDocument d
                    UNION SELECT l.WarehouseId, l.LegacyShardKey, l.LegacyBranchId FROM svc_StockLot l
                    UNION SELECT m.WarehouseId, m.LegacyShardKey, m.LegacyBranchId FROM svc_StockMovement m
                    UNION SELECT s.WarehouseId, s.LegacyShardKey, s.LegacyBranchId FROM svc_Sale s
                ) r
                JOIN svc_Warehouse w ON w.Id = r.Id
                WHERE w.LegacyShardKey IS NULL OR w.LegacyBranchId IS NULL;

                DECLARE @badWarehouse nvarchar(30) = (
                    SELECT TOP 1 w.Code FROM svc_Warehouse w
                    WHERE w.Id IN (SELECT Id FROM @warehouseRefs GROUP BY Id HAVING COUNT(*) > 1)
                    ORDER BY w.Code);
                IF @badWarehouse IS NOT NULL
                BEGIN
                    DECLARE @warehouseMessage nvarchar(2048) = CONCAT(
                        N'ScopeMasterDataByBranch: warehouse ', @badWarehouse,
                        N' has no branch and is used by more than one branch - set svc_Warehouse.LegacyShardKey/LegacyBranchId for it before migrating.');
                    THROW 50000, @warehouseMessage, 1;
                END;

                UPDATE w SET w.LegacyShardKey = r.LegacyShardKey, w.LegacyBranchId = r.LegacyBranchId
                FROM svc_Warehouse w JOIN @warehouseRefs r ON r.Id = w.Id;

                UPDATE svc_Warehouse SET LegacyShardKey = N'db2', LegacyBranchId = 227
                WHERE LegacyShardKey IS NULL OR LegacyBranchId IS NULL;
                """);

            migrationBuilder.DropIndex(
                name: "UX_svc_Supplier_Code",
                table: "svc_Supplier");

            migrationBuilder.DropIndex(
                name: "UX_svc_CatalogCategory_Code",
                table: "svc_CatalogCategory");

            migrationBuilder.DropIndex(
                name: "UX_svc_Warehouse_Code",
                table: "svc_Warehouse");

            migrationBuilder.AlterColumn<string>(
                name: "LegacyShardKey",
                table: "svc_Supplier",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "LegacyBranchId",
                table: "svc_Supplier",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LegacyShardKey",
                table: "svc_CatalogCategory",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "LegacyBranchId",
                table: "svc_CatalogCategory",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LegacyShardKey",
                table: "svc_Warehouse",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "LegacyBranchId",
                table: "svc_Warehouse",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_svc_Supplier_Code",
                table: "svc_Supplier",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_svc_CatalogCategory_Code",
                table: "svc_CatalogCategory",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_svc_Warehouse_Code",
                table: "svc_Warehouse",
                columns: new[] { "LegacyShardKey", "LegacyBranchId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_svc_Supplier_Code",
                table: "svc_Supplier");

            migrationBuilder.DropIndex(
                name: "UX_svc_CatalogCategory_Code",
                table: "svc_CatalogCategory");

            migrationBuilder.DropIndex(
                name: "UX_svc_Warehouse_Code",
                table: "svc_Warehouse");

            migrationBuilder.DropColumn(
                name: "LegacyShardKey",
                table: "svc_Supplier");

            migrationBuilder.DropColumn(
                name: "LegacyBranchId",
                table: "svc_Supplier");

            migrationBuilder.DropColumn(
                name: "LegacyShardKey",
                table: "svc_CatalogCategory");

            migrationBuilder.DropColumn(
                name: "LegacyBranchId",
                table: "svc_CatalogCategory");

            // คลังเก็บสาขาไว้ตามเดิม (แค่กลับเป็น nullable) — ไม่ทิ้งข้อมูลที่มีอยู่ก่อน migration นี้
            migrationBuilder.AlterColumn<string>(
                name: "LegacyShardKey",
                table: "svc_Warehouse",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<int>(
                name: "LegacyBranchId",
                table: "svc_Warehouse",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            // คืน unique ทั้งตาราง — จะล้มถ้าระหว่างนี้มีสองสาขาใช้รหัสเดียวกัน (ซึ่งคือสิ่งที่ migration นี้เปิดให้ทำ)
            // ต้องเปลี่ยนรหัสที่ชนเองก่อนย้อน
            migrationBuilder.CreateIndex(
                name: "UX_svc_Supplier_Code",
                table: "svc_Supplier",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_svc_CatalogCategory_Code",
                table: "svc_CatalogCategory",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_svc_Warehouse_Code",
                table: "svc_Warehouse",
                column: "Code",
                unique: true);
        }
    }
}
