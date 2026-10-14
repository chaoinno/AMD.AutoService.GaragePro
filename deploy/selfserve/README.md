# ฐานข้อมูล self-serve (shard `sp1`) — runbook การ provision

ชุดสคริปต์สร้างฐาน **GarageSelfServe** (production) / **GarageSelfServe_Dev** (dev) สำหรับอู่ที่สมัครทดลองใช้เอง
อ้างอิง: แผน §2–§3 และ `docs/12-self-serve-trial-proposal.md` §2.3

```
GarageSelfServe (instance เดียวกับ Garage = 10.10.4.11 · COLLATE Thai_CI_AS · RCSI on)
├─ dbo.* 16 ตาราง "รูปร่างเหมือน Garage"  ← 02-shadow-ddl.generated.sql (สคริปต์นี้)
├─ synonym 15 ตัว → Garage.dbo.*           ← 04-synonyms.sql (สคริปต์นี้)
├─ svc_* (migration ชุดเดียวกับ GarageService) ← efbundle --connection … (deploy) / API Development
└─ sp_*  (SelfServeDbContext · __EFMigrationsHistory_SelfServe) ← efbundle-selfserve (deploy) / API Development
```

> **GarageService และ Garage ไม่ถูกแก้ข้อมูลเลย** — ข้อยกเว้นเดียวคือ `06-logins.sql` สร้าง user `gp_selfserve_app`
> ในฐาน Garage พร้อม `GRANT SELECT` เฉพาะ 15 ตารางที่ synonym ชี้ (เป็นการแก้ metadata ด้านสิทธิ์ ไม่ใช่ข้อมูล — ต้องให้เจ้าของระบบอนุมัติ)

## ใครรัน · ต้องมีอะไร

| | |
|---|---|
| ผู้รัน | เจ้าของระบบ/DBA เท่านั้น (agent ห้ามรัน — เป็นการสร้างฐานบน instance production) |
| บัญชี | sysadmin (`sa`) ผ่าน Garage Pro VPN · รหัสผ่านทาง env `SQLCMDPASSWORD` หรือพิมพ์ตอนสคริปต์ถาม (ไม่แสดงผล) |
| เครื่องมือ | `sqlcmd` — go-sqlcmd (`brew install sqlcmd`, แนะนำ) หรือ mssql-tools18 (ODBC Driver 18 ต้องโหลด OpenSSL 3 ได้: ถ้า `/opt/homebrew/opt/openssl` ชี้ openssl@4 จะได้ "OpenSSL library could not be loaded") · `bash` |
| รหัสผ่าน login ใหม่ | `SELFSERVE_APP_PASSWORD`, `SELFSERVE_MIGRATE_PASSWORD` (ห้ามมี `'` หรือ `$(` · ผ่าน CHECK_POLICY ของ Windows/SQL) |

## ลำดับ

1. **(ไม่บังคับ) ตรวจว่า schema ของ Garage ยังตรงกับไฟล์ที่ commit**: `CHECK_SHADOW_DDL=1` — สคริปต์จะรัน
   `01-generate-shadow-ddl.sql` กับ Garage (อ่าน `sys.*` อย่างเดียว) แล้ว diff กับ `02-shadow-ddl.generated.sql`
2. **Provision** (idempotent — รันซ้ำได้):
   ```bash
   cd deploy/selfserve
   ./provision-selfserve-db.sh GarageSelfServe_Dev 10.10.4.11      # dev ก่อนเสมอ
   ./provision-selfserve-db.sh GarageSelfServe     10.10.4.11      # production
   ```
   ลำดับภายใน: preflight (Garage มีจริง/Thai_CI_AS/ผู้รันเป็น sysadmin) → `03` สร้างฐาน (master) → `02` ตาราง shadow →
   `04` synonym → `05` คัดลอกตารางอ้างอิง + `Gender(3,'ไม่ระบุ')` → `06` login/user/สิทธิ์ → `07` smoke test
   **ด้วย login ของแอป** (พิสูจน์สิทธิ์ SELECT ผ่าน synonym ไปพร้อมกัน) · ขั้นใดล้ม = หยุดทันที (exit ≠ 0)
3. **ตาราง svc_* + sp_*** — สคริปต์นี้ไม่สร้าง:
   - production: `scripts/deploy-production.sh` รัน `efbundle` (svc_* → GarageSelfServe) และ `efbundle-selfserve` (sp_*)
     เมื่อ env file มี `SelfServe__ConnectionString` (ใช้ `SelfServe__MigrationConnectionString` = login `gp_selfserve_migrate`
     ถ้ามี — ไม่งั้นใช้ connection ของแอปซึ่งไม่มีสิทธิ์ DDL)
   - dev: ตั้ง user-secrets แล้วรัน API โหมด Development — `Program.cs` migrate ทั้งสองชุดลง sp1 ตอนสตาร์ท
     (connection ใน dev ต้องมีสิทธิ์ DDL: ใช้ `gp_selfserve_migrate` หรือ sa)
4. **ตั้งค่า API** (dotnet user-secrets บน Mac → `scripts/bootstrap-production-secrets.sh` → env file):
   | คีย์ | ค่า |
   |---|---|
   | `SelfServe:ConnectionString` | `Server=10.10.4.11;Database=GarageSelfServe_Dev;User Id=gp_selfserve_app;Password=…;TrustServerCertificate=True;Encrypt=False;` |
   | `LegacyShards:ConnectionStrings:sp1` | ค่าเดียวกับบรรทัดบน (Dapper ของ Branch/Staff/Customer/Car อ่านจากคีย์นี้) |
   | `Production:SelfServe:ConnectionString` | connection ของ **GarageSelfServe** (production) — bootstrap ใช้คีย์นี้ ไม่ใช่คีย์ dev ด้านบน |
   | `Production:SelfServe:MigrationConnectionString` | connection ของ `gp_selfserve_migrate` สำหรับ efbundle ตอน deploy |
5. ตรวจ: `GARAGEPRO_SELFSERVE_SQL_CONNECTION="<connection ของแอป>" dotnet test backend/AMD.AutoService.GaragePro.Tests --filter FullyQualifiedName~SelfServeShardSqlTests`
   — รัน SQL จริงของ `LegacyUserReader`/`StaffRepository`/`CustomerVehicleRepository`/ตัวนับลิมิต กับฐานนี้ (อ่านอย่างเดียว)

## ข้อตกลงที่ WP อื่นต้องรู้

- **เวลาในคอลัมน์แพ็กเกจของ Branch (`StartPackageDate`/`EndPackageDate`) = เวลาไทย** แบบ legacy (db2 เก็บเที่ยงคืนเวลาไทย)
  — เขียน/อ่านผ่าน `BranchPackageDates.ToBranchLocal/ToUtc` (`Application/SelfServe/PlanPolicy.cs`) เท่านั้น ·
  ตาราง `sp_*` เป็น UTC ทั้งหมด
- **`EndPackageDate` เป็นตัวตัดสินวันหมดอายุตัวเดียว** — paid แบบไม่มีกำหนด = `NULL` (ไม่คำนวณจาก Start+Days)
- enum ใน `sp_*` เก็บเป็นชื่อ (เช่น `Trial`, `Active`) — โค้ด Dapper ที่เขียนตารางเหล่านี้ต้องส่ง `.ToString()`
- ห้าม `IsAdministrator=1` ในฐานนี้ (แผน §2)
- identity ของตาราง shadow เริ่มที่ 1 เหมือนต้นทาง (Branch.Id ของ sp1 ซ้ำตัวเลขกับ db2 ได้ — ทุกที่ต้องพก shard เสมออยู่แล้ว)

## Rollback

ฐานนี้แยกขาดจาก GarageService — rollback เต็มคือทิ้งฐาน (ปิดรับสมัครก่อนด้วย `SelfServe__Enabled=false`
และลบ `SelfServe__ConnectionString` ออกจาก env file แล้ว deploy ใหม่ ไม่งั้น API จะต่อฐานที่หายไป):

```sql
-- รันด้วย sysadmin
ALTER DATABASE [GarageSelfServe] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [GarageSelfServe];
USE [Garage]; DROP USER IF EXISTS [gp_selfserve_app];      -- ถอน GRANT SELECT 15 ตารางไปพร้อมกัน
USE [master]; DROP LOGIN [gp_selfserve_app]; DROP LOGIN [gp_selfserve_migrate];
```
(ไฟล์แนบของอู่ทดลองใช้อยู่บน FTP ใต้ `sp1/…` — ลบแยกถ้าต้องการ)

## ไฟล์

| ไฟล์ | รันที่ | ทำอะไร |
|---|---|---|
| `01-generate-shadow-ddl.sql` | Garage (อ่านอย่างเดียว) | generator — สร้างเนื้อหา `02` จาก `sys.*` (compat level 100 ได้) |
| `02-shadow-ddl.generated.sql` | GarageSelfServe* | **ไฟล์ที่ generate แล้ว ห้ามแก้มือ** · 16 ตาราง · 15 index · 20 FK |
| `03-create-database.sql` | master | `CREATE DATABASE … COLLATE Thai_CI_AS` + RCSI + compat 150 |
| `04-synonyms.sql` | GarageSelfServe* | synonym 15 ตัว (ชี้ผิดฐาน = สร้างใหม่) |
| `05-copy-reference-rows.sql` | GarageSelfServe* | Position/Sector/Department/StaffSkillLevel/Gender/RepairTypeNPoint (คง Id) + Gender 3 |
| `06-logins.sql` | master → GarageSelfServe* → Garage | login/user/สิทธิ์ (ดูหัวไฟล์) |
| `07-smoke.sql` | GarageSelfServe* (ด้วย login แอป) | ตรวจ collation/RCSI/ตาราง/synonym/สิทธิ์/ตารางอ้างอิง |
| `provision-selfserve-db.sh` | — | ตัวรันทั้งชุด |

### สิ่งที่ generator ตัด/คงไว้ (ณ schema db2 2026-10-14)
- คงไว้: ทุกคอลัมน์ของ 15 ตาราง (ชนิด/ความยาว/NULL/`COLLATE Thai_CI_AS`/`IDENTITY(1,1) NOT FOR REPLICATION`) · PK ·
  index ทุกตัวรวม `IX_User` (unique, `UserName` nvarchar(50) — ไม่ขยาย; อีเมลแปลงผ่าน `sp_Account`) · `DF_Customer_IsBlacklist`
- `PJCarPickUp` เหลือ 4 คอลัมน์ (`Id`, `CustomerId`, `CarId`, `BranchId`) ที่ `BranchDataScope` อ่าน + index `ix_CarId` —
  index อื่น 14 ตัวอ้างคอลัมน์ที่ถูกตัดจึงไม่สร้าง (ตารางนี้ว่างตลอดใน sp1: จ๊อบอยู่ที่ `svc_Job`)
- FK: 20 ตัว — เฉพาะที่ทั้งสองฝั่งอยู่ในชุด (FK ไปตารางอ้างอิงที่เป็น synonym/ตารางอื่นของ Garage สร้างไม่ได้)
  · FK ของ `PJCarPickUp` ไป `User` (CreatedBy ฯลฯ) ตัดเพราะคอลัมน์ไม่ได้สร้าง · `RepairTypeNPoint → PJStatus` ตัด
- ตาราง `Gender`/`StaffSkillLevel` ไม่มี identity (เหมือนต้นทาง) · `StaffRepairTypeNPoint` PK = (`StaffId`, `RepairTypeNPointId`)
