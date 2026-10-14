/*
  05-copy-reference-rows.sql — คัดลอกตารางอ้างอิงของพนักงานจาก Garage ครั้งเดียว (เล็กมาก: Position 10 · Sector 76 ·
  Department 6 · StaffSkillLevel 4 · Gender 2 · RepairTypeNPoint 34 แถว ณ 2026-10-13) โดยคง Id เดิม
  รันในฐาน GarageSelfServe(_Dev) · sqlcmd variables: GarageDb

  · ทำไมคัดลอก ไม่ใช่ synonym: ฟอร์มพนักงานอ่านตาม shard ของ JWT และ Staff/StaffSectorPosition มี FK มาที่ตารางเหล่านี้
    (FK ชี้ synonym ไม่ได้) · id ต้องตรง db2 เพราะ B5 ผูก Position/Sector ตามบทบาทด้วย id ของ db2 และ S5 ย้ายกลับ db2
  · Gender(3, 'ไม่ระบุ') เพิ่มเฉพาะใน sp1 — พนักงานที่เจ้าของอู่สร้างไม่กรอกเพศ (API เติม GenderId=3) · Garage มีแค่ 1/2
    ใส่ก่อนคัดลอกเพื่อให้ id 3 หมายถึง "ไม่ระบุ" เสมอแม้วันหนึ่ง Garage จะเพิ่ม id 3 ของตัวเอง (แถวนั้นจะถูกข้าม)
    ข้อความไทยประกอบจาก NCHAR() ให้ผลเหมือนกันไม่ว่า sqlcmd จะอ่านไฟล์ด้วย code page ไหน
  · idempotent: แทรกเฉพาะ Id ที่ยังไม่มี (ไม่แก้แถวเดิม — ถ้าต้องการ sync ชื่อจาก Garage ให้ทำแยกพร้อมทบทวน)
  · ทั้งไฟล์เป็น transaction เดียว (XACT_ABORT) — ล้มกลางทางไม่เหลือข้อมูลครึ่งๆ
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() NOT LIKE N'GarageSelfServe%'
BEGIN
    RAISERROR (N'05-copy-reference-rows: run this inside a GarageSelfServe database only', 16, 1);
    RETURN;
END;

IF DB_ID(N'$(GarageDb)') IS NULL
BEGIN
    RAISERROR (N'05-copy-reference-rows: source database $(GarageDb) not found on this instance', 16, 1);
    RETURN;
END;

BEGIN TRANSACTION;

-- Gender(3, N'ไม่ระบุ', 1)
IF NOT EXISTS (SELECT 1 FROM dbo.Gender WHERE Id = 3)
    INSERT INTO dbo.Gender (Id, Name, Status)
    VALUES (3, NCHAR(0x0E44) + NCHAR(0x0E21) + NCHAR(0x0E48) + NCHAR(0x0E23) + NCHAR(0x0E30) + NCHAR(0x0E1A) + NCHAR(0x0E38), 1);

INSERT INTO dbo.Gender (Id, Name, Status)
SELECT s.Id, s.Name, s.Status
FROM [$(GarageDb)].dbo.Gender s WITH (READUNCOMMITTED)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Gender t WHERE t.Id = s.Id);

INSERT INTO dbo.StaffSkillLevel (Id, Name, Status)
SELECT s.Id, s.Name, s.Status
FROM [$(GarageDb)].dbo.StaffSkillLevel s WITH (READUNCOMMITTED)
WHERE NOT EXISTS (SELECT 1 FROM dbo.StaffSkillLevel t WHERE t.Id = s.Id);

-- Department ก่อน Sector (FK_Sector_Department)
SET IDENTITY_INSERT dbo.Department ON;
INSERT INTO dbo.Department (Id, Name, Status)
SELECT s.Id, s.Name, s.Status
FROM [$(GarageDb)].dbo.Department s WITH (READUNCOMMITTED)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Department t WHERE t.Id = s.Id);
SET IDENTITY_INSERT dbo.Department OFF;

SET IDENTITY_INSERT dbo.Sector ON;
INSERT INTO dbo.Sector (Id, Name, Status, DepartmentId)
SELECT s.Id, s.Name, s.Status, s.DepartmentId
FROM [$(GarageDb)].dbo.Sector s WITH (READUNCOMMITTED)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Sector t WHERE t.Id = s.Id);
SET IDENTITY_INSERT dbo.Sector OFF;

SET IDENTITY_INSERT dbo.Position ON;
INSERT INTO dbo.Position (Id, Name, AbbreviationName, SubOfId, CreatedDate, LastUpdated, Status)
SELECT s.Id, s.Name, s.AbbreviationName, s.SubOfId, s.CreatedDate, s.LastUpdated, s.Status
FROM [$(GarageDb)].dbo.Position s WITH (READUNCOMMITTED)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Position t WHERE t.Id = s.Id);
SET IDENTITY_INSERT dbo.Position OFF;

-- PJStatusId ชี้ PJStatus ของ Garage (ไม่มี FK ในฐานนี้ — PJStatus ไม่อยู่ในชุด) คงค่าเดิมไว้
SET IDENTITY_INSERT dbo.RepairTypeNPoint ON;
INSERT INTO dbo.RepairTypeNPoint (Id, Name, Point, CreatedDate, Status, PJStatusId)
SELECT s.Id, s.Name, s.Point, s.CreatedDate, s.Status, s.PJStatusId
FROM [$(GarageDb)].dbo.RepairTypeNPoint s WITH (READUNCOMMITTED)
WHERE NOT EXISTS (SELECT 1 FROM dbo.RepairTypeNPoint t WHERE t.Id = s.Id);
SET IDENTITY_INSERT dbo.RepairTypeNPoint OFF;

COMMIT TRANSACTION;

SELECT N'Gender' AS tbl, COUNT(*) AS rows_in_selfserve FROM dbo.Gender
UNION ALL SELECT N'StaffSkillLevel', COUNT(*) FROM dbo.StaffSkillLevel
UNION ALL SELECT N'Department', COUNT(*) FROM dbo.Department
UNION ALL SELECT N'Sector', COUNT(*) FROM dbo.Sector
UNION ALL SELECT N'Position', COUNT(*) FROM dbo.Position
UNION ALL SELECT N'RepairTypeNPoint', COUNT(*) FROM dbo.RepairTypeNPoint;
