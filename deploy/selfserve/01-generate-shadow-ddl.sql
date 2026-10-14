/*
  01-generate-shadow-ddl.sql — สร้าง DDL ของตาราง "รูปร่างเหมือน Garage" 16 ตัวสำหรับฐาน GarageSelfServe
  ---------------------------------------------------------------------------------------------------
  รันกับฐาน Garage (shard db2) — อ่าน sys.* อย่างเดียว · ไม่แตะข้อมูล · ไม่มี DDL/DML กับตารางจริง
  (มีแค่ #temp ของ session ตัวเอง) · เขียนให้รันได้ที่ compatibility level 100 ของ Garage (ไม่ใช้ STRING_AGG)

  ผลลัพธ์ = หนึ่งแถวต่อหนึ่งบรรทัดของไฟล์ 02-shadow-ddl.generated.sql (deterministic — schema เดิมได้ไฟล์เดิมทุกไบต์)

  สร้างใหม่เมื่อ schema ของ Garage เปลี่ยน หรือคลาส Dapper ใน Infrastructure/Legacy อ่านตาราง/คอลัมน์เพิ่ม:
    sqlcmd -S 10.10.4.11 -U sa -d Garage -C -b -h -1 -y 0 -f 65001 \
           -i 01-generate-shadow-ddl.sql -o 02-shadow-ddl.generated.sql
  (รหัสผ่านผ่าน env SQLCMDPASSWORD เท่านั้น) แล้ว diff ไฟล์ที่ได้ก่อน commit

  สิ่งที่คงไว้ตามต้นทาง: ชนิด/ความยาว · NULL/NOT NULL · IDENTITY(seed,increment) + NOT FOR REPLICATION ·
  COLLATE ของคอลัมน์ข้อความ (Thai_CI_AS ทั้งหมด) · PK · index ทุกตัว (รวม IX_User unique บน UserName) ที่คอลัมน์ครบ ·
  default constraint · FK เฉพาะที่ทั้งสองฝั่งอยู่ใน 16 ตารางนี้ (FK ไปตารางอ้างอิงที่เป็น synonym สร้างไม่ได้ — ตัดทิ้ง)
  สิ่งที่ตัด: PJCarPickUp เหลือแค่คอลัมน์ที่ BranchDataScope อ่าน (Id, BranchId, CustomerId, CarId) — sp1 ไม่ใช้ตารางนี้
  (จ๊อบอยู่ที่ svc_Job) แต่คิวรีขอบเขตลูกค้า/รถยัง UNION จากมัน · index ที่อ้างคอลัมน์ที่ถูกตัดไม่ถูกสร้าง
  generator หยุดด้วย error ถ้าเจอสิ่งที่ยังไม่รองรับ (computed/sparse/rowguid/UDT/ตารางไม่มี PK) — กันได้ DDL ผิดแบบเงียบๆ
  ข้อความในไฟล์ที่สร้างเป็น ASCII ทั้งหมดโดยตั้งใจ (ไม่ขึ้นกับ code page ของ sqlcmd)
*/
SET NOCOUNT ON;

DECLARE @tables TABLE (
    ord int NOT NULL PRIMARY KEY,
    name sysname COLLATE DATABASE_DEFAULT NOT NULL,
    onlyColumns nvarchar(4000) COLLATE DATABASE_DEFAULT NULL);

-- ลำดับ = ลำดับในไฟล์ (FK ถูกเพิ่มท้ายไฟล์ จึงไม่ต้องเรียงตาม dependency)
INSERT INTO @tables (ord, name, onlyColumns) VALUES
    (1, N'Branch', NULL),
    (2, N'Gender', NULL),
    (3, N'Department', NULL),
    (4, N'Sector', NULL),
    (5, N'Position', NULL),
    (6, N'StaffSkillLevel', NULL),
    (7, N'Staff', NULL),
    (8, N'User', NULL),
    (9, N'StaffSectorPosition', NULL),
    (10, N'RepairTypeNPoint', NULL),
    (11, N'StaffRepairTypeNPoint', NULL),
    (12, N'Customer', NULL),
    (13, N'Car', NULL),
    (14, N'CarCustomer', NULL),
    (15, N'ChangeLog', NULL),
    (16, N'PJCarPickUp', N'|Id|BranchId|CustomerId|CarId|');

IF EXISTS (SELECT 1 FROM @tables t WHERE OBJECT_ID(N'dbo.' + QUOTENAME(t.name), N'U') IS NULL)
BEGIN
    RAISERROR (N'01-generate-shadow-ddl: source table missing - run this against the Garage database', 16, 1);
    RETURN;
END;

IF EXISTS (
    SELECT 1
    FROM @tables t
    JOIN sys.columns c ON c.object_id = OBJECT_ID(N'dbo.' + QUOTENAME(t.name))
    JOIN sys.types ut ON ut.user_type_id = c.user_type_id
    WHERE (t.onlyColumns IS NULL OR CHARINDEX(N'|' + c.name + N'|', t.onlyColumns) > 0)
      AND (c.is_computed = 1 OR c.is_sparse = 1 OR c.is_rowguidcol = 1 OR c.is_column_set = 1 OR ut.is_user_defined = 1))
BEGIN
    RAISERROR (N'01-generate-shadow-ddl: unsupported column (computed/sparse/rowguid/user-defined type) - extend the generator', 16, 1);
    RETURN;
END;

IF EXISTS (
    SELECT 1 FROM @tables t
    WHERE NOT EXISTS (SELECT 1 FROM sys.indexes i
                      WHERE i.object_id = OBJECT_ID(N'dbo.' + QUOTENAME(t.name)) AND i.is_primary_key = 1))
BEGIN
    RAISERROR (N'01-generate-shadow-ddl: table without primary key - extend the generator', 16, 1);
    RETURN;
END;

-- คอลัมน์ที่จะสร้าง + นิยามคอลัมน์ (COLLATE DATABASE_DEFAULT ทุกค่าข้อความจาก catalog กัน collation conflict:
-- #temp ใช้ collation ของ tempdb = ของ server ซึ่งไม่ใช่ Thai_CI_AS)
CREATE TABLE #cols (
    tbl sysname COLLATE DATABASE_DEFAULT NOT NULL,
    column_id int NOT NULL,
    def nvarchar(4000) COLLATE DATABASE_DEFAULT NOT NULL,
    PRIMARY KEY (tbl, column_id));

INSERT INTO #cols (tbl, column_id, def)
SELECT
    t.name,
    c.column_id,
    QUOTENAME(c.name COLLATE DATABASE_DEFAULT) + N' '
    + CASE
        WHEN ty.name IN (N'nvarchar', N'nchar')
            THEN ty.name COLLATE DATABASE_DEFAULT + N'('
                 + CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length / 2 AS nvarchar(10)) END + N')'
        WHEN ty.name IN (N'varchar', N'char', N'varbinary', N'binary')
            THEN ty.name COLLATE DATABASE_DEFAULT + N'('
                 + CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length AS nvarchar(10)) END + N')'
        WHEN ty.name IN (N'decimal', N'numeric')
            THEN ty.name COLLATE DATABASE_DEFAULT + N'(' + CAST(c.precision AS nvarchar(10)) + N','
                 + CAST(c.scale AS nvarchar(10)) + N')'
        WHEN ty.name IN (N'datetime2', N'time', N'datetimeoffset')
            THEN ty.name COLLATE DATABASE_DEFAULT + N'(' + CAST(c.scale AS nvarchar(10)) + N')'
        WHEN ty.name = N'float' AND c.precision <> 53
            THEN N'float(' + CAST(c.precision AS nvarchar(10)) + N')'
        ELSE ty.name COLLATE DATABASE_DEFAULT
      END
    + CASE WHEN c.collation_name IS NULL THEN N''
           ELSE N' COLLATE ' + c.collation_name COLLATE DATABASE_DEFAULT END
    + CASE WHEN ic.column_id IS NULL THEN N''
           ELSE N' IDENTITY(' + CAST(ic.seed_value AS nvarchar(40)) + N',' + CAST(ic.increment_value AS nvarchar(40)) + N')'
                + CASE WHEN ic.is_not_for_replication = 1 THEN N' NOT FOR REPLICATION' ELSE N'' END END
    + CASE WHEN c.is_nullable = 1 THEN N' NULL' ELSE N' NOT NULL' END
    + CASE WHEN dc.object_id IS NULL THEN N''
           WHEN dc.is_system_named = 1 THEN N' DEFAULT ' + dc.definition COLLATE DATABASE_DEFAULT
           ELSE N' CONSTRAINT ' + QUOTENAME(dc.name COLLATE DATABASE_DEFAULT) + N' DEFAULT '
                + dc.definition COLLATE DATABASE_DEFAULT END
FROM @tables t
JOIN sys.columns c ON c.object_id = OBJECT_ID(N'dbo.' + QUOTENAME(t.name))
JOIN sys.types ty ON ty.user_type_id = c.system_type_id
LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
WHERE t.onlyColumns IS NULL OR CHARINDEX(N'|' + c.name + N'|', t.onlyColumns) > 0;

CREATE TABLE #out (seq int IDENTITY(1, 1) NOT NULL PRIMARY KEY, line nvarchar(max) COLLATE DATABASE_DEFAULT NOT NULL);

INSERT INTO #out (line) VALUES
    (N'-- 02-shadow-ddl.generated.sql - GENERATED by 01-generate-shadow-ddl.sql. Do not edit by hand: change the generator and regenerate.'),
    (N'-- Source: Garage (shard db2). 16 Garage-shaped tables read/written by Infrastructure/Legacy (Dapper).'),
    (N'-- PJCarPickUp keeps only the columns BranchDataScope reads. FKs only where both ends are in this set.'),
    (N'-- Run inside GarageSelfServe / GarageSelfServe_Dev only. Idempotent: existing objects are skipped.'),
    (N'SET ANSI_NULLS ON;'),
    (N'SET QUOTED_IDENTIFIER ON;'),
    (N'GO'),
    (N'IF DB_NAME() NOT LIKE N''GarageSelfServe%'''),
    (N'BEGIN'),
    (N'    RAISERROR (N''02-shadow-ddl: run this inside a GarageSelfServe database only'', 16, 1);'),
    (N'    SET NOEXEC ON;'),
    (N'END;'),
    (N'GO');

DECLARE @ord int = 0;
DECLARE @name sysname;
DECLARE @obj int;
DECLARE @quoted nvarchar(300);
DECLARE @literal nvarchar(600);

WHILE 1 = 1
BEGIN
    SELECT TOP (1) @ord = ord, @name = name FROM @tables WHERE ord > @ord ORDER BY ord;
    IF @@ROWCOUNT = 0 BREAK;

    SET @obj = OBJECT_ID(N'dbo.' + QUOTENAME(@name));
    SET @quoted = N'[dbo].' + QUOTENAME(@name);
    SET @literal = REPLACE(@quoted, N'''', N'''''');

    INSERT INTO #out (line) VALUES
        (N''),
        (N'-- ' + @quoted),
        (N'IF OBJECT_ID(N''' + @literal + N''', N''U'') IS NULL'),
        (N'BEGIN'),
        (N'    CREATE TABLE ' + @quoted + N' (');

    INSERT INTO #out (line)
    SELECT N'        ' + def + N',' FROM #cols WHERE tbl = @name ORDER BY column_id;

    INSERT INTO #out (line)
    SELECT N'        CONSTRAINT ' + QUOTENAME(i.name COLLATE DATABASE_DEFAULT) + N' PRIMARY KEY '
           + CASE WHEN i.type = 1 THEN N'CLUSTERED' ELSE N'NONCLUSTERED' END + N' ('
           + STUFF((SELECT N', ' + QUOTENAME(c.name COLLATE DATABASE_DEFAULT)
                          + CASE WHEN ic.is_descending_key = 1 THEN N' DESC' ELSE N' ASC' END
                    FROM sys.index_columns ic
                    JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                    WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
                    ORDER BY ic.key_ordinal
                    FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N'')
           + N')'
    FROM sys.indexes i
    WHERE i.object_id = @obj AND i.is_primary_key = 1;

    INSERT INTO #out (line) VALUES (N'    );'), (N'END;'), (N'GO');

    -- index อื่น (รวม unique constraint) — ข้ามตัวที่อ้างคอลัมน์ที่ไม่ได้สร้าง (มีเฉพาะ PJCarPickUp)
    INSERT INTO #out (line)
    SELECT x.line
    FROM (
        SELECT i.name COLLATE DATABASE_DEFAULT AS iname, 1 AS part,
               N'IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''' + @literal
               + N''') AND name = N''' + REPLACE(i.name COLLATE DATABASE_DEFAULT, N'''', N'''''') + N''')' AS line
        FROM sys.indexes i
        WHERE i.object_id = @obj AND i.type IN (1, 2) AND i.is_primary_key = 0 AND i.is_hypothetical = 0
          AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic
                          WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                            AND NOT EXISTS (SELECT 1 FROM #cols k WHERE k.tbl = @name AND k.column_id = ic.column_id))
        UNION ALL
        SELECT i.name COLLATE DATABASE_DEFAULT, 2,
               N'    '
               + CASE WHEN i.is_unique_constraint = 1
                      THEN N'ALTER TABLE ' + @quoted + N' ADD CONSTRAINT ' + QUOTENAME(i.name COLLATE DATABASE_DEFAULT)
                           + N' UNIQUE ' + CASE WHEN i.type = 1 THEN N'CLUSTERED' ELSE N'NONCLUSTERED' END
                           + N' (' + k.keys + N');'
                      ELSE N'CREATE ' + CASE WHEN i.is_unique = 1 THEN N'UNIQUE ' ELSE N'' END
                           + CASE WHEN i.type = 1 THEN N'CLUSTERED' ELSE N'NONCLUSTERED' END
                           + N' INDEX ' + QUOTENAME(i.name COLLATE DATABASE_DEFAULT) + N' ON ' + @quoted
                           + N' (' + k.keys + N')'
                           + ISNULL(N' INCLUDE (' + k.incl + N')', N'')
                           + ISNULL(N' WHERE ' + i.filter_definition COLLATE DATABASE_DEFAULT, N'') + N';'
                 END
        FROM sys.indexes i
        CROSS APPLY (
            SELECT
                STUFF((SELECT N', ' + QUOTENAME(c.name COLLATE DATABASE_DEFAULT)
                              + CASE WHEN ic.is_descending_key = 1 THEN N' DESC' ELSE N' ASC' END
                       FROM sys.index_columns ic
                       JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                       WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
                       ORDER BY ic.key_ordinal
                       FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N'') AS keys,
                STUFF((SELECT N', ' + QUOTENAME(c.name COLLATE DATABASE_DEFAULT)
                       FROM sys.index_columns ic
                       JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                       WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1
                       ORDER BY ic.index_column_id
                       FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N'') AS incl
        ) k
        WHERE i.object_id = @obj AND i.type IN (1, 2) AND i.is_primary_key = 0 AND i.is_hypothetical = 0
          AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic
                          WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                            AND NOT EXISTS (SELECT 1 FROM #cols k2 WHERE k2.tbl = @name AND k2.column_id = ic.column_id))
    ) x
    ORDER BY x.iname, x.part;

    IF EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id = @obj AND i.type IN (1, 2) AND i.is_primary_key = 0)
        INSERT INTO #out (line) VALUES (N'GO');
END;

-- FK: ทั้งสองฝั่งอยู่ในชุด และคอลัมน์ฝั่งลูกถูกสร้างครบ (FK ของ PJCarPickUp ไป User ถูกตัดเพราะคอลัมน์ CreatedBy ฯลฯ ไม่ได้สร้าง)
INSERT INTO #out (line) VALUES (N''), (N'-- foreign keys (both ends inside this table set)');

INSERT INTO #out (line)
SELECT x.line
FROM (
    SELECT fk.name COLLATE DATABASE_DEFAULT AS fkname, 1 AS part,
           N'IF OBJECT_ID(N''[dbo].' + REPLACE(QUOTENAME(fk.name COLLATE DATABASE_DEFAULT), N'''', N'''''')
           + N''', N''F'') IS NULL' AS line
    FROM sys.foreign_keys fk
    JOIN @tables pt ON OBJECT_ID(N'dbo.' + QUOTENAME(pt.name)) = fk.parent_object_id
    JOIN @tables rt ON OBJECT_ID(N'dbo.' + QUOTENAME(rt.name)) = fk.referenced_object_id
    WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_key_columns fkc
                      WHERE fkc.constraint_object_id = fk.object_id
                        AND NOT EXISTS (SELECT 1 FROM #cols k WHERE k.tbl = pt.name AND k.column_id = fkc.parent_column_id))
    UNION ALL
    SELECT fk.name COLLATE DATABASE_DEFAULT, 2,
           N'    ALTER TABLE [dbo].' + QUOTENAME(pt.name) + N' WITH CHECK ADD CONSTRAINT '
           + QUOTENAME(fk.name COLLATE DATABASE_DEFAULT) + N' FOREIGN KEY ('
           + STUFF((SELECT N', ' + QUOTENAME(c.name COLLATE DATABASE_DEFAULT)
                    FROM sys.foreign_key_columns fkc
                    JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
                    WHERE fkc.constraint_object_id = fk.object_id
                    ORDER BY fkc.constraint_column_id
                    FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N'')
           + N') REFERENCES [dbo].' + QUOTENAME(rt.name) + N' ('
           + STUFF((SELECT N', ' + QUOTENAME(c.name COLLATE DATABASE_DEFAULT)
                    FROM sys.foreign_key_columns fkc
                    JOIN sys.columns c ON c.object_id = fkc.referenced_object_id AND c.column_id = fkc.referenced_column_id
                    WHERE fkc.constraint_object_id = fk.object_id
                    ORDER BY fkc.constraint_column_id
                    FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N'')
           + N')'
           + CASE fk.delete_referential_action WHEN 1 THEN N' ON DELETE CASCADE' WHEN 2 THEN N' ON DELETE SET NULL'
                                               WHEN 3 THEN N' ON DELETE SET DEFAULT' ELSE N'' END
           + CASE fk.update_referential_action WHEN 1 THEN N' ON UPDATE CASCADE' WHEN 2 THEN N' ON UPDATE SET NULL'
                                               WHEN 3 THEN N' ON UPDATE SET DEFAULT' ELSE N'' END
           + N';'
    FROM sys.foreign_keys fk
    JOIN @tables pt ON OBJECT_ID(N'dbo.' + QUOTENAME(pt.name)) = fk.parent_object_id
    JOIN @tables rt ON OBJECT_ID(N'dbo.' + QUOTENAME(rt.name)) = fk.referenced_object_id
    WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_key_columns fkc
                      WHERE fkc.constraint_object_id = fk.object_id
                        AND NOT EXISTS (SELECT 1 FROM #cols k WHERE k.tbl = pt.name AND k.column_id = fkc.parent_column_id))
) x
ORDER BY x.fkname, x.part;

INSERT INTO #out (line) VALUES (N'GO'), (N'SET NOEXEC OFF;'), (N'GO');

SELECT line FROM #out ORDER BY seq;

DROP TABLE #out;
DROP TABLE #cols;
