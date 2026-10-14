/*
  07-smoke.sql — ตรวจฐาน self-serve หลัง provision (อ่านอย่างเดียว ไม่เขียนแถวใดๆ)
  provision-selfserve-db.sh รันไฟล์นี้ "ด้วย login ของแอป" (gp_selfserve_app) — จึงพิสูจน์สิทธิ์ไปพร้อมกัน:
  อ่านตารางรูปร่างเหมือน Garage ได้ · อ่านผ่าน synonym ข้ามไป Garage ได้ (GRANT SELECT) · ไม่ชน collation
  ล้มข้อไหน = RAISERROR severity 16 (sqlcmd -b หยุดและคืน exit code ≠ 0)
*/
SET NOCOUNT ON;

IF DB_NAME() NOT LIKE N'GarageSelfServe%'
    RAISERROR (N'smoke: not connected to a GarageSelfServe database', 16, 1);

IF CONVERT(sysname, DATABASEPROPERTYEX(DB_NAME(), 'Collation')) <> N'Thai_CI_AS'
    RAISERROR (N'smoke: database collation is not Thai_CI_AS', 16, 1);

IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = DB_NAME() AND is_read_committed_snapshot_on = 1)
    RAISERROR (N'smoke: READ_COMMITTED_SNAPSHOT is off', 16, 1);

-- 16 ตารางรูปร่างเหมือน Garage
DECLARE @missing nvarchar(2000) = N'';  -- RAISERROR รับ (max) เป็นอาร์กิวเมนต์ไม่ได้
SELECT @missing = @missing + t.name + N' '
FROM (VALUES (N'Branch'), (N'User'), (N'Staff'), (N'StaffSectorPosition'), (N'Position'), (N'Sector'),
             (N'Department'), (N'StaffSkillLevel'), (N'Gender'), (N'Customer'), (N'Car'), (N'CarCustomer'),
             (N'PJCarPickUp'), (N'RepairTypeNPoint'), (N'StaffRepairTypeNPoint'), (N'ChangeLog')) t(name)
WHERE OBJECT_ID(N'[dbo].' + QUOTENAME(t.name), N'U') IS NULL;
IF @missing <> N''
    RAISERROR (N'smoke: missing shadow tables: %s', 16, 1, @missing);

-- 15 synonym: มีจริง + อ่านได้ด้วยสิทธิ์ของผู้รัน + นับแถว (ตารางเล็ก ทุกตัว < 10,000 แถว)
DECLARE @report TABLE (name sysname NOT NULL, row_count bigint NULL);
DECLARE @name sysname;
DECLARE @sql nvarchar(max);
DECLARE @count bigint;

DECLARE smoke_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM (VALUES (N'province'), (N'amphures'), (N'districts'), (N'zipcodes'), (N'BrandCar'),
                             (N'CarModel'), (N'CarNickName'), (N'Year'), (N'CarType'), (N'PrimaryColor'),
                             (N'ColorMix'), (N'Gear'), (N'Machine'), (N'DriveSystem'), (N'InsuranceType')) s(name);
OPEN smoke_cursor;
FETCH NEXT FROM smoke_cursor INTO @name;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(N'[dbo].' + QUOTENAME(@name), N'SN') IS NULL
        RAISERROR (N'smoke: synonym dbo.%s is missing', 16, 1, @name);
    ELSE
    BEGIN
        SET @sql = N'SELECT @c = COUNT_BIG(*) FROM [dbo].' + QUOTENAME(@name) + N' WITH (READUNCOMMITTED);';
        EXEC sys.sp_executesql @sql, N'@c bigint OUTPUT', @c = @count OUTPUT;
        INSERT INTO @report (name, row_count) VALUES (@name, @count);
        IF @count = 0
            RAISERROR (N'smoke: synonym dbo.%s returns no rows', 16, 1, @name);
    END;
    FETCH NEXT FROM smoke_cursor INTO @name;
END;
CLOSE smoke_cursor;
DEALLOCATE smoke_cursor;

-- ตารางอ้างอิงที่คัดลอกมา + Gender(3) ที่ API ใช้กับพนักงานที่ไม่ระบุเพศ
IF NOT EXISTS (SELECT 1 FROM dbo.Gender WHERE Id = 3)
    RAISERROR (N'smoke: Gender(3) is missing - run 05-copy-reference-rows.sql', 16, 1);
IF (SELECT COUNT(*) FROM dbo.Position) = 0 OR (SELECT COUNT(*) FROM dbo.Sector) = 0
   OR (SELECT COUNT(*) FROM dbo.Department) = 0 OR (SELECT COUNT(*) FROM dbo.StaffSkillLevel) = 0
    RAISERROR (N'smoke: reference rows missing - run 05-copy-reference-rows.sql', 16, 1);

-- join ข้อความข้ามฐาน (คอลัมน์ shadow ↔ ตารางของ Garage ผ่าน synonym) ต้องไม่ชน collation — ถ้าชนจะ error ที่บรรทัดนี้
SELECT @count = COUNT_BIG(*)
FROM dbo.province p WITH (READUNCOMMITTED)
LEFT JOIN dbo.Branch b WITH (READUNCOMMITTED) ON b.Name = p.PROVINCE_NAME;

-- รูปแบบเดียวกับคิวรีของแอป: ข้อมูลพนักงาน + ที่อยู่ผ่าน synonym
SELECT TOP (1) @count = s.Id
FROM dbo.Staff s WITH (READUNCOMMITTED)
JOIN dbo.Branch b WITH (READUNCOMMITTED) ON b.Id = s.BranchId
LEFT JOIN dbo.Gender g WITH (READUNCOMMITTED) ON g.Id = s.GenderId
LEFT JOIN dbo.province pr WITH (READUNCOMMITTED) ON pr.PROVINCE_ID = s.ProvinceId
LEFT JOIN dbo.amphures a WITH (READUNCOMMITTED) ON a.AMPHUR_ID = s.AmphureId
LEFT JOIN dbo.districts di WITH (READUNCOMMITTED) ON di.DISTRICT_ID = s.DistrictId;

SELECT name AS synonym_name, row_count FROM @report ORDER BY name;
SELECT N'Gender' AS reference_table, COUNT(*) AS row_count FROM dbo.Gender
UNION ALL SELECT N'Position', COUNT(*) FROM dbo.Position
UNION ALL SELECT N'Sector', COUNT(*) FROM dbo.Sector
UNION ALL SELECT N'Department', COUNT(*) FROM dbo.Department
UNION ALL SELECT N'StaffSkillLevel', COUNT(*) FROM dbo.StaffSkillLevel
UNION ALL SELECT N'RepairTypeNPoint', COUNT(*) FROM dbo.RepairTypeNPoint;
PRINT N'SMOKE OK: ' + DB_NAME();
