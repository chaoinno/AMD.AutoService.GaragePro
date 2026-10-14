/*
  03-create-database.sql — สร้างฐาน GarageSelfServe(_Dev) · รันกับ master (provision-selfserve-db.sh ส่ง -d master)
  sqlcmd variables: DbName

  · COLLATE Thai_CI_AS เหมือน Garage — ตารางรูปร่างเหมือน Garage + synonym ไปตารางอ้างอิงของ Garage ต้อง join กันได้
    (GarageService เป็น SQL_Latin1_General_CP1_CI_AS — ถ้าฐานนี้ใช้ collation ของ server จะชน collation ทุก join ที่เป็นข้อความ)
  · READ_COMMITTED_SNAPSHOT ON เหมือน GarageService (svc_* ทั้งชุดออกแบบมากับ RCSI)
  · idempotent: ฐานมีอยู่แล้ว = ตรวจ collation (ไม่ตรง = หยุด ไม่แก้ให้เอง) แล้วตั้งค่าที่ขาด
*/
SET NOCOUNT ON;

IF N'$(DbName)' NOT LIKE N'GarageSelfServe%'
BEGIN
    RAISERROR (N'03-create-database: DbName must start with GarageSelfServe', 16, 1);
    RETURN;
END;

IF DB_ID(N'$(DbName)') IS NULL
BEGIN
    PRINT N'Creating database $(DbName) COLLATE Thai_CI_AS';
    CREATE DATABASE [$(DbName)] COLLATE Thai_CI_AS;
END
ELSE
    PRINT N'Database $(DbName) already exists - checking settings only';

IF (SELECT collation_name FROM sys.databases WHERE name = N'$(DbName)') <> N'Thai_CI_AS'
BEGIN
    RAISERROR (N'03-create-database: $(DbName) exists with a collation other than Thai_CI_AS - drop and recreate it (see README rollback)', 16, 1);
    RETURN;
END;

IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'$(DbName)' AND is_read_committed_snapshot_on = 0)
BEGIN
    PRINT N'Enabling READ_COMMITTED_SNAPSHOT on $(DbName)';
    ALTER DATABASE [$(DbName)] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
END;

-- EF Core 8 + filtered index/DateOnly ใช้ได้ทุกระดับ แต่ตั้งให้เท่า GarageService (150) เพื่อให้พฤติกรรม optimizer เหมือนกัน
IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'$(DbName)' AND compatibility_level < 150)
BEGIN
    PRINT N'Setting COMPATIBILITY_LEVEL = 150 on $(DbName)';
    ALTER DATABASE [$(DbName)] SET COMPATIBILITY_LEVEL = 150;
END;

SELECT name, collation_name, is_read_committed_snapshot_on, compatibility_level, recovery_model_desc
FROM sys.databases
WHERE name = N'$(DbName)';
