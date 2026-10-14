/*
  04-synonyms.sql — synonym 15 ตัวไปตารางอ้างอิงของ Garage (ไม่คัดลอก — id ตรงกับ db2 เสมอ)
  รันในฐาน GarageSelfServe(_Dev) · sqlcmd variables: GarageDb (ค่าเริ่มต้นใน provision script = Garage)

  · ต้องอยู่ instance เดียวกับ Garage (synonym ข้าม instance ใช้ไม่ได้กับ Dapper SQL ที่ใช้ table hint)
  · คิวรี Dapper อ้างชื่อเหล่านี้ตรงๆ (province, [Year], BrandCar …) ทั้งใน JOIN ของลูกค้า/รถ/พนักงาน และ dropdown
  · idempotent: มีแล้วและชี้ถูกที่ = ข้าม · ชี้ผิดฐาน = drop แล้วสร้างใหม่ · ตารางปลายทางไม่มี = หยุดด้วย error
*/
SET NOCOUNT ON;

IF DB_NAME() NOT LIKE N'GarageSelfServe%'
BEGIN
    RAISERROR (N'04-synonyms: run this inside a GarageSelfServe database only', 16, 1);
    RETURN;
END;

DECLARE @garage sysname = N'$(GarageDb)';

IF DB_ID(@garage) IS NULL
BEGIN
    RAISERROR (N'04-synonyms: source database %s not found on this instance', 16, 1, @garage);
    RETURN;
END;

DECLARE @names TABLE (name sysname NOT NULL PRIMARY KEY);
INSERT INTO @names (name) VALUES
    (N'province'), (N'amphures'), (N'districts'), (N'zipcodes'),
    (N'BrandCar'), (N'CarModel'), (N'CarNickName'), (N'Year'), (N'CarType'),
    (N'PrimaryColor'), (N'ColorMix'), (N'Gear'), (N'Machine'), (N'DriveSystem'), (N'InsuranceType');

DECLARE @name sysname;
DECLARE @target nvarchar(400);
DECLARE @sql nvarchar(max);

DECLARE synonym_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM @names ORDER BY name;
OPEN synonym_cursor;
FETCH NEXT FROM synonym_cursor INTO @name;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @target = QUOTENAME(@garage) + N'.[dbo].' + QUOTENAME(@name);

    IF OBJECT_ID(@target, N'U') IS NULL
    BEGIN
        RAISERROR (N'04-synonyms: target table %s not found', 16, 1, @target);
        CLOSE synonym_cursor;
        DEALLOCATE synonym_cursor;
        RETURN;
    END;

    IF EXISTS (SELECT 1 FROM sys.synonyms
               WHERE schema_id = SCHEMA_ID(N'dbo') AND name = @name AND base_object_name <> @target)
    BEGIN
        PRINT N'Re-pointing synonym dbo.' + @name + N' -> ' + @target;
        SET @sql = N'DROP SYNONYM [dbo].' + QUOTENAME(@name) + N';';
        EXEC sys.sp_executesql @sql;
    END;

    IF OBJECT_ID(N'[dbo].' + QUOTENAME(@name), N'SN') IS NULL
    BEGIN
        PRINT N'Creating synonym dbo.' + @name + N' -> ' + @target;
        SET @sql = N'CREATE SYNONYM [dbo].' + QUOTENAME(@name) + N' FOR ' + @target + N';';
        EXEC sys.sp_executesql @sql;
    END;

    FETCH NEXT FROM synonym_cursor INTO @name;
END;

CLOSE synonym_cursor;
DEALLOCATE synonym_cursor;

SELECT s.name, s.base_object_name
FROM sys.synonyms s
WHERE s.schema_id = SCHEMA_ID(N'dbo')
ORDER BY s.name;
