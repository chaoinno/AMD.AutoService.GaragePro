/*
  06-logins.sql — login ของแอปและของ migration สำหรับฐาน self-serve (รันด้วยบัญชี sysadmin เท่านั้น)
  sqlcmd variables: DbName · GarageDb · AppLogin (gp_selfserve_app) · MigrateLogin (gp_selfserve_migrate)
                    SELFSERVE_APP_PASSWORD · SELFSERVE_MIGRATE_PASSWORD
  [SECURITY] รหัสผ่านมาจาก environment variable (sqlcmd ใช้ env เป็น scripting variable) — ห้ามส่งผ่าน -v/บรรทัดคำสั่ง
  (จะเห็นใน ps) และห้าม commit · รหัสผ่านห้ามมี ' (แทนค่าแบบข้อความตรงๆ) — provision-selfserve-db.sh ตรวจให้แล้ว

  สิทธิ์ (least privilege):
  · AppLogin  = ที่ API ใช้ (SelfServe:ConnectionString / LegacyShards:ConnectionStrings:sp1)
      ฐาน self-serve: db_datareader + db_datawriter + EXECUTE (sp_getapplock/MERGE ของ svc_* ใช้ได้ด้วยสิทธิ์นี้)
      ฐาน Garage:    user ที่มี SELECT เฉพาะ 15 ตารางที่ synonym ชี้ (ownership chaining ข้ามฐานปิดอยู่ จึงต้อง GRANT ตรง)
                     — [RISK] การสร้าง user + GRANT ใน Garage เป็นการแก้ metadata ของฐาน legacy (ไม่ใช่ข้อมูล) ต้องให้เจ้าของระบบอนุมัติ
  · MigrateLogin = ที่ efbundle ใช้ตอน deploy: ฐาน self-serve เท่านั้น — db_ddladmin + db_datareader + db_datawriter
      (migration ของ svc_* มี UPDATE backfill) · ไม่มีสิทธิ์ใดใน Garage
  idempotent: login/user/สิทธิ์ที่มีอยู่แล้วถูกข้าม · ไม่เปลี่ยนรหัสผ่านของ login ที่มีอยู่แล้ว (เปลี่ยนรหัสทำแยกด้วย ALTER LOGIN)
*/
SET NOCOUNT ON;

USE [master];

-- หลายแบตช์ (GO) — RETURN หยุดแค่แบตช์นี้ จึงใช้ NOEXEC กันแบตช์ที่เหลือไม่ให้ทำงานเมื่อรันโดยไม่มี sqlcmd -b
IF N'$(DbName)' NOT LIKE N'GarageSelfServe%'
BEGIN
    RAISERROR (N'06-logins: DbName must start with GarageSelfServe', 16, 1);
    SET NOEXEC ON;
END;

IF SUSER_ID(N'$(AppLogin)') IS NULL
BEGIN
    PRINT N'Creating login $(AppLogin)';
    CREATE LOGIN [$(AppLogin)] WITH PASSWORD = N'$(SELFSERVE_APP_PASSWORD)',
        DEFAULT_DATABASE = [$(DbName)], CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
END
ELSE
    PRINT N'Login $(AppLogin) exists - password left unchanged';

IF SUSER_ID(N'$(MigrateLogin)') IS NULL
BEGIN
    PRINT N'Creating login $(MigrateLogin)';
    CREATE LOGIN [$(MigrateLogin)] WITH PASSWORD = N'$(SELFSERVE_MIGRATE_PASSWORD)',
        DEFAULT_DATABASE = [$(DbName)], CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
END
ELSE
    PRINT N'Login $(MigrateLogin) exists - password left unchanged';
GO

USE [$(DbName)];

IF DATABASE_PRINCIPAL_ID(N'$(AppLogin)') IS NULL
    CREATE USER [$(AppLogin)] FOR LOGIN [$(AppLogin)] WITH DEFAULT_SCHEMA = [dbo];
IF IS_ROLEMEMBER(N'db_datareader', N'$(AppLogin)') = 0
    ALTER ROLE [db_datareader] ADD MEMBER [$(AppLogin)];
IF IS_ROLEMEMBER(N'db_datawriter', N'$(AppLogin)') = 0
    ALTER ROLE [db_datawriter] ADD MEMBER [$(AppLogin)];
GRANT EXECUTE TO [$(AppLogin)];

IF DATABASE_PRINCIPAL_ID(N'$(MigrateLogin)') IS NULL
    CREATE USER [$(MigrateLogin)] FOR LOGIN [$(MigrateLogin)] WITH DEFAULT_SCHEMA = [dbo];
IF IS_ROLEMEMBER(N'db_ddladmin', N'$(MigrateLogin)') = 0
    ALTER ROLE [db_ddladmin] ADD MEMBER [$(MigrateLogin)];
IF IS_ROLEMEMBER(N'db_datareader', N'$(MigrateLogin)') = 0
    ALTER ROLE [db_datareader] ADD MEMBER [$(MigrateLogin)];
IF IS_ROLEMEMBER(N'db_datawriter', N'$(MigrateLogin)') = 0
    ALTER ROLE [db_datawriter] ADD MEMBER [$(MigrateLogin)];
GO

USE [$(GarageDb)];

IF DATABASE_PRINCIPAL_ID(N'$(AppLogin)') IS NULL
    CREATE USER [$(AppLogin)] FOR LOGIN [$(AppLogin)] WITH DEFAULT_SCHEMA = [dbo];

-- เฉพาะ 15 ตารางที่ synonym ในฐาน self-serve ชี้มา — ไม่ให้ role ใดๆ ไม่ให้ตารางอื่น
GRANT SELECT ON OBJECT::[dbo].[province] TO [$(AppLogin)];
GRANT SELECT ON OBJECT::[dbo].[amphures] TO [$(AppLogin)];
GRANT SELECT ON OBJECT::[dbo].[districts] TO [$(AppLogin)];
GRANT SELECT ON OBJECT::[dbo].[zipcodes] TO [$(AppLogin)];
GRANT SELECT ON OBJECT::[dbo].[BrandCar] TO [$(AppLogin)];
GRANT SELECT ON OBJECT::[dbo].[CarModel] TO [$(AppLogin)];
GRANT SELECT ON OBJECT::[dbo].[CarNickName] TO [$(AppLogin)];
GRANT SELECT ON OBJECT::[dbo].[Year] TO [$(AppLogin)];
GRANT SELECT ON OBJECT::[dbo].[CarType] TO [$(AppLogin)];
GRANT SELECT ON OBJECT::[dbo].[PrimaryColor] TO [$(AppLogin)];
GRANT SELECT ON OBJECT::[dbo].[ColorMix] TO [$(AppLogin)];
GRANT SELECT ON OBJECT::[dbo].[Gear] TO [$(AppLogin)];
GRANT SELECT ON OBJECT::[dbo].[Machine] TO [$(AppLogin)];
GRANT SELECT ON OBJECT::[dbo].[DriveSystem] TO [$(AppLogin)];
GRANT SELECT ON OBJECT::[dbo].[InsuranceType] TO [$(AppLogin)];
GO

USE [master];
SELECT p.name AS login_name, p.default_database_name, p.is_disabled
FROM sys.server_principals p
WHERE p.name IN (N'$(AppLogin)', N'$(MigrateLogin)');
SET NOEXEC OFF;
