# spadmin — เครื่องมือทีมงานของอู่ทดลองใช้ (shard `sp1`)

เครื่องมือ command line สำหรับทีมงาน ServicePro ใช้ดูและจัดการอู่ที่สมัครทดลองใช้เอง (ฐาน `GarageSelfServe`)
และย้ายข้อมูลหลักของลูกค้าที่จ่ายเงินแล้วไปเป็นสาขา GaragePro จริงใน db2

- ใช้ DI ชุดเดียวกับ API (`AddGarageProInfrastructure`) — กฎธุรกิจ/guard ทั้งหมดอยู่ใน `SelfServeAdminService`,
  `ConversionService`, `IWorkspacePurger`, `ITrialLifecycleService` ไม่มีกติกาซ่อนใน CLI
- ทุกคำสั่งที่เปลี่ยนข้อมูลลง `sp_WorkspaceAudit` พร้อมชื่อผู้กระทำ (`--by` หรือ `$USER` → `spadmin:<ชื่อ>`)
- `purge` และ `convert` (ที่ไม่ใช่ `--dry-run`) ต้องใส่ `--yes` เสมอ
- งานรายวัน (hosted job) **ไม่** วิ่งใน spadmin — รันเฉพาะเมื่อสั่ง `run-jobs`

## ค่าตั้ง

ลำดับ (ตัวหลังชนะ): `appsettings.json` ข้างไฟล์โปรแกรม → dotnet user-secrets ของ API (`UserSecretsId` เดียวกับ API และ
`tools/devseed` · อ่านเฉพาะนอก container) → ตัวแปรแวดล้อม

ต้องมีอย่างน้อย:

| ค่า | ใช้ทำอะไร |
|---|---|
| `SelfServe:ConnectionString` | ฐาน GarageSelfServe (sp_* · svc_* ของ sp1 · ตารางรูปร่าง Garage) |
| `LegacyShards:ConnectionStrings:sp1` | ค่าเดียวกับบรรทัดบน (Dapper อ่าน/เขียนแถว Branch/ลูกค้า/รถ/พนักงานของ sp1) |
| `ConnectionStrings:ServiceDb` · `LegacyShards:ConnectionStrings:db2` | เฉพาะ `convert` (เขียนสาขาปลายทาง) และ guard ที่ตรวจว่าฐาน self-serve ไม่ใช่ฐานจริง |
| `Ftp:*` | เฉพาะ `purge`/`run-jobs` ที่ลบไฟล์แนบ |
| `SelfServe:Email:*` · `LineMessaging:*` · `SelfServe:SalesLineGroupId` | เฉพาะ `run-jobs` (ส่งอีเมล/LINE จริง ถ้าตั้งไว้) · `approve-payment`/`reject-payment` ส่งอีเมลถึงเจ้าของอู่ |
| `SelfServe:Package:*` | `payment` (ลิงก์ดูสลิปต้องมี `SlipLinkKey` ≥ 32 ตัว) · ราคามาตรฐานที่ใช้เทียบยอด (`MonthlyPrice` 3,999 + `VatRate` 0.07 และบัญชีรับเงินอยู่ใน `appsettings.json` ของ spadmin แล้ว ค่าเดียวกับ API) |

ไม่ได้ตั้ง `SelfServe:ConnectionString` → ทุกคำสั่ง (ยกเว้น `--help`) จบด้วยข้อความบอกค่าที่ขาด และรหัสออก 3 โดยไม่ต่อฐานใดๆ

## วิธีรัน

**เครื่อง dev** (ต่อ VPN ก่อน — ระวัง: ฐาน dev ของ GarageService = ฐาน production):

```bash
dotnet run --project tools/spadmin -- --help
dotnet run --project tools/spadmin -- list --status active
```

**production** (ใน image ของ API ที่ `/app/spadmin` — ใช้ env file เดียวกับ API ไม่ต้องมีไฟล์ค่าตั้งเพิ่ม):

```bash
ssh garage_amd
cd /home/deployment/deployments/garagepro-service
export GARAGEPRO_SOURCE_DIR=/home/deployment/sources/AMD.AutoService.GaragePro
export GARAGEPRO_DEPLOY_DIR=$PWD
export GARAGEPRO_SECRETS_FILE=/home/deployment/config/garagepro-service.env

docker compose run --rm --no-deps --entrypoint dotnet api /app/spadmin/SpAdmin.dll list
# รูปแบบเดียวกัน: docker compose run --rm --no-deps --entrypoint "dotnet /app/spadmin/SpAdmin.dll" api list
```

ใน container ไม่มี `$USER` ของทีมงาน (ผู้ใช้คือ `appuser`) — **ใส่ `--by <ชื่อ>` ทุกครั้งที่เปลี่ยนข้อมูล** ให้ audit บอกได้ว่าใครทำ ·
`--no-deps` = ไม่สตาร์ท service อื่น · container ทำงานที่ `/app` จึงเห็น volume รูปพนักงาน/รถ (`/app/storage`) แบบเดียวกับ API
(`purge` ลบรูปของอู่จากที่นั่น) · `convert --out` เขียนไฟล์ใน container — ใช้ `-v "$PWD:/out"` แล้ว `--out /out/report.json`
ถ้าต้องการเก็บไฟล์ไว้นอก container

## คำสั่ง

| คำสั่ง | ทำอะไร |
|---|---|
| `list [--status provisioning\|active\|expired\|purged] [--plan trial\|paid\|suspended\|converted]` | ตาราง: รหัสอู่ · ชื่อ · โทร · แพ็กเกจ/สถานะ · วันหมดอายุ · เหลือกี่วัน · วันสมัคร · อีเมลเจ้าของ |
| `show --workspace SP1042` | รายละเอียดอู่ · บัญชีเจ้าของ (ไม่มี hash) · วันแพ็กเกจใน Branch · การใช้ลิมิต · audit 15 รายการล่าสุด |
| `extend --workspace N --days D [--note ...]` | `EndPackageDate += D` (ติดลบได้) พร้อมสำเนา `sp_Workspace.TrialEndsAt` · Expired → Active ถ้าวันใหม่ยังไม่ถึง |
| `set-plan --workspace N --plan trial\|paid\|suspended [--until yyyy-MM-dd] [--note ...]` | paid = ทุกฟีเจอร์ ไม่จำกัด · ไม่ใส่ `--until` = ไม่มีกำหนด · `--until` = ใช้ได้ถึง**สิ้นวัน**นั้นเวลาไทย |
| `suspend --workspace N --reason "..."` | ระงับทันที (เข้าสู่ระบบไม่ได้ ได้ข้อความ `TRIAL_EXPIRED`) · ข้อมูลยังอยู่ · ยกเลิกด้วย `set-plan` |
| `purge --workspace N --yes [--allow-paid]` | ลบข้อมูลอู่ทั้งหมดทันที (svc_* · ไฟล์แนบ · ลูกค้า/รถ/พนักงาน) คงไว้แค่อีเมลที่ normalize แล้วกันสมัครซ้ำ |
| `run-jobs [--task maintenance\|summary] [--as-of ...]` | รันงานรายวันทันทีโดยไม่ claim (`sp_JobRun`) · `--as-of` = ISO `2026-12-31T03:30:00+07:00` · วันที่ `2026-12-31` (เวลางานรายวัน) · `+91d`/`-1d` · ไม่มี offset = เวลาไทย · `--date` = ชื่อเดิม |
| `convert --workspace N --target-branch M --target-user U [--target-shard db2] [--dry-run] [--out report.json] [--allow-failures] [--yes]` | ย้ายข้อมูลหลักไปสาขา GaragePro จริง (ดูหัวข้อถัดไป) |
| `payments [--status pending\|approved\|rejected\|cancelled\|all] [--workspace SP1042]` | รายการแจ้งชำระ (ค่าเริ่มต้นรอตรวจ) · id สั้น · ยอดโอน/ราคา · เวลาโอน · "ถ้าอนุมัติตอนนี้ใช้ได้ถึง" · สลิปซ้ำ |
| `payment --id <id หรือ 8 ตัวแรก>` | รายละเอียด · หมายเหตุลูกค้า · สลิปซ้ำ · วันหมดใหม่ถ้าอนุมัติตอนนี้ · ราคาตามค่าตั้งตอนนี้ (ก่อน VAT + VAT = รวม) ของรายการรอตรวจ · ลิงก์ดูสลิป (อายุ 7 วัน) · คำสั่งอนุมัติ/ปฏิเสธที่คัดลอกได้ |
| `approve-payment --id <...> --by <ชื่อ> [--until yyyy-MM-dd] [--note ...]` | ต่อวันหมด + เปลี่ยนเป็น paid + ปิดรายการ + audit ใน transaction เดียว · ยอดไม่ตรงราคาต้องมี `--note` · `--until` ต้องไม่ก่อนวันเริ่มนับ · ส่งอีเมล/แจ้งเตือนเจ้าของอู่ |
| `reject-payment --id <...> --by <ชื่อ> --reason slip_unreadable\|amount_mismatch\|not_received\|duplicate\|other [--note ...]` | ปฏิเสธพร้อมเหตุผล (`other` ต้องมี `--note`) · ลูกค้าเห็นเหตุผล+หมายเหตุ · ส่งอีเมล/แจ้งเตือนเจ้าของอู่ |

ตัวอย่าง:

```bash
spadmin list --plan trial --status active
spadmin show --workspace SP1042
spadmin extend --workspace SP1042 --days 14 --note "ลูกค้าขอทดลองต่อ" --by noi
spadmin extend --workspace SP1042 --days -31          # จำลองหมดอายุ (แผน §6 #8)
spadmin set-plan --workspace SP1042 --plan paid --until 2027-12-31
spadmin set-plan --workspace SP1042 --plan suspended   # หรือ suspend --reason "..."
spadmin run-jobs --task maintenance --as-of +91d      # จำลองวันลบ (แผน §6 #10)
spadmin purge --workspace SP1042 --yes
spadmin payments                                       # รายการแจ้งชำระที่รอตรวจ
spadmin payment --id 3f2a9c1b                          # id สั้นจากข้อความ LINE
spadmin approve-payment --id 3f2a9c1b --by noi
spadmin approve-payment --id 3f2a9c1b --by noi --note "ส่วนลดเปิดตัว"   # ยอดไม่ตรงราคา
spadmin reject-payment --id 3f2a9c1b --by noi --reason not_received
```

### แจ้งชำระ/ต่ออายุ (docs/17)

- สูตรวัน: เริ่มนับจากวันหมดเดิมที่ปัดขึ้นเป็นเที่ยงคืนไทย (หรือวันนี้ถ้าหมดแล้ว) แล้วบวกจำนวนเดือนตามปฏิทิน ไม่บวก 1 วัน
- `--by` **บังคับ** สำหรับ approve/reject (อนุมัติเรื่องเงิน · ใน container `$USER` ไม่มีความหมาย) · `--id` สั้นกว่า 8 ตัวหรือตรงหลายรายการ = ปฏิเสธพร้อมรายการที่ตรง
- อนุมัติพร้อมกันสองคนได้ผลครั้งเดียว · มีคน `extend` วันหมดระหว่างอนุมัติ = `PACKAGE_APPROVAL_CONFLICT` ไม่บันทึกอะไร ให้รันใหม่
- อู่ที่ถูกระงับต้อง `set-plan` ยกเลิกการระงับก่อน · อู่ที่มีรายการรอตรวจไม่ถูก `purge` จนกว่าจะตรวจ (หรือใส่ `--allow-paid`)

รหัสออก: `0` สำเร็จ · `1` ถูกปฏิเสธ/ล้ม · `2` คำสั่งผิดหรือขาด `--yes` · `3` ไม่มีค่าตั้งฐาน self-serve ·
`4` ทำจบแต่มีบางรายการล้ม (convert/run-jobs) · `130` ยกเลิกด้วย Ctrl+C

การเปลี่ยนแพ็กเกจมีผลกับ API ภายใน ~60 วินาที (cache ของนโยบายแพ็กเกจ/การตรวจเซสชัน)

## convert — ย้ายลูกค้าที่จ่ายเงินไปเป็นสาขา GaragePro

ย้าย**เฉพาะข้อมูลหลัก** ตามลำดับ: หมวดหมู่ (แม่ก่อนลูก) → คลัง (เพื่อผูกสินค้า) → สินค้า/บริการ → เทมเพลตใบเสนอราคา →
พนักงาน → ลูกค้า → รถ (ผูกเจ้าของ) · **ไม่ย้าย** จ๊อบ · ใบเสนอราคา · ใบเสร็จ/การชำระเงิน · เวลาทำงานช่าง ·
ซัพพลายเออร์/ใบสั่งซื้อ/ล็อตสต็อก · รูปภาพ (รายงานบอกว่ามีรูปไหนบ้างที่ต้องอัปโหลดใหม่)

ขั้นตอน:

1. เจ้าของอู่ตกลงจ่ายเงินและอนุมัติการย้ายแล้ว
2. สร้างสาขาใน **GaragePro Admin** (db2 · กลุ่ม 7 "Services") และบัญชีพนักงานของเจ้าของอู่ในสาขานั้น — จด `Branch.Id` (M) และ `[User].Id` (U)
3. **รัน `--dry-run` ก่อนเสมอ** แล้วอ่านรายงาน (ไม่เขียนอะไรเลยทั้งปลายทาง/ledger/audit):
   ```bash
   spadmin convert --workspace SP1042 --target-branch 431 --target-user 98765 --dry-run --out sp1042-dry.json
   ```
4. แก้สิ่งที่ล้ม (ส่วนใหญ่คือข้อมูลต้นทางไม่ครบ เช่น เบอร์โทรผิดรูปแบบ · รถไม่มีปี/โฉม) แล้วรัน dry-run ซ้ำจนพอใจ
5. ย้ายจริง:
   ```bash
   spadmin convert --workspace SP1042 --target-branch 431 --target-user 98765 --yes --out sp1042.json --by noi
   ```
6. ส่งชื่อผู้ใช้พนักงานใหม่ให้เจ้าของอู่ (ดูด้านล่าง) · ตรวจข้อมูลที่สาขาปลายทาง · อัปโหลดรูปที่จำเป็นใหม่

กติกา:

- เขียนปลายทางผ่าน service เดิมของระบบในนาม `ScopedCurrentUser(db2, M, U, Manager, Web)` เท่านั้น — validation/ActivityEvent
  เหมือนผู้จัดการกดเองบนเว็บ · การเขียนลูกค้า/รถ/พนักงานลง Garage ใช้ทางเขียนเดิม ไม่มีคำสั่ง SQL ใหม่
- **ของที่มีอยู่แล้วที่ปลายทางไม่ถูกสร้างซ้ำ:** รหัสหมวดหมู่/คลัง/สินค้า/เทมเพลตซ้ำ → ใช้ของเดิม (ไม่แก้ราคา/ชื่อของสาขาจริง) ·
  ลูกค้าชื่อ+นามสกุล+เบอร์ซ้ำ (`CUSTOMER_DUPLICATE`) → ใช้คนเดิม · รถทะเบียนเดียวกันของลูกค้าเดิม → ใช้คันเดิม ·
  พนักงานชื่อซ้ำในสาขาปลายทาง → ใช้คนเดิม · **เจ้าของอู่ไม่ถูกสร้าง** (ใช้บัญชี U ที่สร้างใน GaragePro Admin)
- แถวตัวอย่าง (`sp_SampleEntity`) และแถวที่ปิดใช้งาน/ลบแล้วไม่ย้าย · บรรทัดเทมเพลตที่อ้างสินค้าที่ไม่ได้ย้ายถูกตัดออก
- ทุกแถวที่สำเร็จลง `sp_ConversionMap` ทันที — **รันซ้ำได้เสมอ** แถวที่ย้ายแล้วถูกข้าม (ล้มกลางทาง/Ctrl+C แล้วรันใหม่) ·
  รันซ้ำได้เฉพาะปลายทางเดิม (shard/สาขาอื่นถูกปฏิเสธ)
- ปิดการย้าย (`Plan=converted` · `ConvertedAt` · `ConvertedToBranchId` · `PurgeAfter = +30 วัน`) **เฉพาะเมื่อไม่มีแถวที่ล้ม** —
  converted = อู่ใน sp1 อ่านอย่างเดียว เจ้าของแก้ข้อมูลต้นทางไม่ได้อีก · ยอมรับรายการที่ล้มแล้วปิดเลยด้วย `--allow-failures`
- หลัง `PurgeAfter` งานรายวันลบข้อมูลของอู่ใน sp1 เอง (อู่ converted ต้องยืนยันการลบ — job ยืนยันให้เฉพาะเมื่อเลยกำหนดแล้ว ·
  ลบก่อนกำหนดด้วย `purge --yes --allow-paid`)
- พนักงาน: ตำแหน่ง/หน่วยงานปลายทางจับคู่ด้วย**ชื่อ**ตามบทบาทใน sp1 (ผู้จัดการ → ผู้จัดการ/ผู้จัดการ · ช่าง → พนักงาน/งานซ่อมทั่วไป ·
  หัวหน้าช่าง → หัวหน้าแผนก/ควบคุมงานซ่อม · หน้าร้าน → รับรถ · ธุรการ → ธุรการ · แคชเชียร์ → บัญชี) เพราะสาขา legacy ตัดสินบทบาท
  จากชื่อเหล่านี้ · เพศ "ไม่ระบุ" ของ sp1 ไม่มีใน Garage → ตั้งเป็นค่าแรกของปลายทางพร้อมแจ้งในรายงาน (แก้ใน GaragePro Admin)
- สินค้าที่ใช้ระบบสต็อก FIFO: ยอดคงเหลือคัดลอกเป็นตัวเลข (ไม่ได้ย้ายล็อต) — ตั้งยอดยกมาที่สาขาปลายทางถ้าใช้ระบบสต็อก

### ⚠ ความปลอดภัย

- **ย้ายเฉพาะเมื่อสาขาใน db2 สร้างแล้วและเจ้าของอู่อนุมัติแล้ว · รัน `--dry-run` ก่อนทุกครั้ง** — การย้ายจริงเขียนลงฐาน production
  (Garage + GarageService) และย้อนกลับด้วยเครื่องมือนี้ไม่ได้
- ปฏิเสธเมื่อ: ปลายทางเป็น shard ทดลองใช้เอง · ผู้ใช้ปลายทางไม่ใช่พนักงานที่ active ของสาขานั้น · สาขาไม่ active หรือไม่ใช่กลุ่ม 7 ·
  อู่ไม่ได้อยู่สถานะ Active/Expired · เคยย้ายไปปลายทางอื่นแล้ว
- พนักงานใหม่ที่สาขา legacy ได้ **ชื่อผู้ใช้ = รหัสผ่าน = รหัสพนักงาน** (กติกาเดิมของ GaragePro) — รายงาน (`--out`) จึงมีค่าที่ใช้เข้า
  ระบบได้: เก็บเป็นความลับ ส่งให้เจ้าของอู่แล้วลบทิ้ง และให้พนักงานเปลี่ยนรหัสผ่านทันที
- `purge` ย้อนกลับไม่ได้ · ปฏิเสธเสมอถ้าค่าตั้งชี้ฐานที่ไม่ใช่ GarageSelfServe (guard สองชั้นใน `WorkspacePurger`) · อู่ paid/converted
  ต้อง `--allow-paid`
- `run-jobs` ส่งอีเมล/LINE จริงถ้าตั้งค่าไว้ (dedupe ด้วย `sp_WorkspaceNotice`/retry key จึงไม่ส่งซ้ำ) · `--as-of` ในอนาคตทำให้ลบ
  อู่ที่ "ครบกำหนด ณ วันนั้น" จริง — ใช้กับฐาน dev เท่านั้น
- `extend`/`set-plan` เขียนแถว Branch ของ sp1 ผ่าน connection ของฐาน self-serve และตรวจก่อนว่าค่าตั้งชี้ GarageSelfServe จริง
  (ไม่ใช่ Garage/GarageService) — ไม่เคยแตะ Branch ของ db2
