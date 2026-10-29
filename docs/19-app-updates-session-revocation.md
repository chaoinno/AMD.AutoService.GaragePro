# แจ้งอัปเดตแอปและเพิกถอนเซสชัน (2026-10-28)

สถานะ: implementation ใน working tree; ยังไม่ deploy / migrate ฐานจริง / ส่ง Store

## การใช้งาน

- เว็บ `/admin/app-updates`: บัญชีที่เป็น `IsAdministrator` ทุกบัญชีแก้นโยบาย **ทุกอู่** ได้ ตามการตัดสินใจของเจ้าของระบบ ไม่ใช่เฉพาะสาขาที่เลือก; API ตรวจสถานะ admin สดอีกครั้ง
- แยก iOS/Android ตั้งเปิด/ปิด, รุ่นล่าสุด, รุ่นขั้นต่ำ, ข้อความ รุ่นขั้นต่ำ `0.0.0` หมายถึงแจ้งเตือนทั่วไป ยืนยัน Store พร้อมก่อนเปิด/ยกระดับการบังคับ
- แอป `1.4.0+6` เป็นรุ่นแรกในงานนี้ที่มีตัวตรวจ รุ่นก่อนหน้านี้ยังไม่แสดง popup ต้องอัปเดตมารุ่นนี้ก่อน
- เปรียบเทียบเลข `major.minor.patch` (ไม่เปรียบเทียบ build number) ลิงก์ Store ตายตัว ใช้ package/app ID เดิม
- ตรวจตอนเปิด/กลับเข้าแอป/ทุกนาทีขณะอยู่ foreground จำกัดความถี่หนึ่งนาที ยกเว้นกดตรวจเองหรือกลับจาก Store
- “ไว้ภายหลัง” เลื่อนได้ 24 ชม. ต่อเวอร์ชัน; ขั้นต่ำมีลำดับเหนือการเลื่อน หาก server ตรวจไม่ได้ใช้นโยบายที่สำเร็จล่าสุด ไม่มี cache = ไม่เดาว่าต้องบล็อก
- หน้าเว็บมี preview, history 50 รายการล่าสุด, ปิดนโยบาย และ revision ป้องกันแก้พร้อมกัน

## สัญญา API

- `GET /api/v1/app-updates/{ios|android}`: anonymous, no-store, envelope เดิม; แอปเรียกโดยไม่แนบ token
  · server cache ผลไว้ 30 วินาทีต่อ instance (แอปทุกเครื่องถามทุกนาที) · ผู้ดูแลบันทึกแล้วล้าง cache ของ instance นั้นทันที
- `GET /api/v1/admin/app-updates/{platform}` / `PUT` และ `GET .../{platform}/history`: admin เท่านั้น
- policy: `platform, enabled, latestVersion, minimumVersion, message, revision, storeUrl`
- PUT ใช้ `enabled, latestVersion, minimumVersion, message, revision, storeAvailableConfirmed`; revision ต้องตรงค่าที่อ่านมา
- 403 `APP_UPDATE_FORBIDDEN`, 409 `APP_UPDATE_CONFLICT`, 422 `APP_UPDATE_VERSION|APP_UPDATE_INVALID|APP_UPDATE_CONFIRM_STORE`
- `PATCH /api/v1/staffs/{id}/status` ใช้ body เดิม; เพิ่ม 403 `STAFF_STATUS_FORBIDDEN` สำหรับผู้ที่ไม่ใช่ admin/Manager
  · 409 `STAFF_STATUS_BUSY` เมื่อมีคำขอเปิด/ปิดพนักงานคนเดียวกันค้างอยู่ (รอ lock เกิน 10 วินาที)
  · เขียนฐานเดิมไม่สำเร็จรายงานเป็น error ปกติของ server ไม่ใช่ `AUTH_CHECK_UNAVAILABLE` (รุ่นที่เพิ่มไปแล้วยังอยู่)
- เซสชันถูกยกเลิก = 401 `AUTH_REQUIRED`; ตรวจ store เพิกถอนไม่ได้/กำลังเปลี่ยนสถานะ = 503 `AUTH_CHECK_UNAVAILABLE` ไม่ logout เมื่อเป็น 503

## ข้อมูลและผลกระทบ

- `svc_UserSessionVersion` อยู่ใน ServiceDb ของ shard นั้น PK `(ShardKey, UserId)`; JWT เพิ่ม `session_version` ไม่มี claim = 0
- API อ่าน version ทุกคำขอที่ยืนยันตัวด้วย JWT ไม่ใช้ cache และตรวจ pre-session ก่อนเลือกกะด้วย ข้อมูลนี้ไม่อยู่ใน catch ที่ปล่อยผ่านเมื่อฐาน Staff ล่ม
- ปิด Staff ต้องเป็น admin/Manager ภายในสาขาเดิม; คงข้อจำกัดแก้บัญชี admin ของ repository เดิม
  · ตรวจข้อจำกัดนี้กับ **ทุกบัญชี** ที่ผูกกับพนักงานก่อนเพิ่มรุ่น (`IStaffRepository.GetAccountIdsAsync` คืน null) เพราะรุ่นถูกเพิ่มก่อนที่ `SetStatusAsync` จะได้ปฏิเสธ
- อ่าน User IDs ที่ผูกกับ Staff ทั้งหมด ตั้งรุ่นใหม่พร้อม flag `Changing` ใน transaction ของ ServiceDb ก่อนเปลี่ยน Staff ในฐานเดิม ใช้ SQL application lock ต่อ Staff ครอบทั้งสองขั้นเพื่อกันเปิด/ปิดพร้อมกัน
- ระหว่างเปลี่ยนสถานะไม่ออก token ใหม่ หาก legacy write ล้ม รุ่นที่เพิ่มไปยังคงอยู่ แล้วปลด flag ให้ผู้ที่ยัง active login ใหม่ได้; การออก token อ่าน version ก่อนอ่านสถานะ active ใหม่ เพื่อกัน race กับการปิดบัญชี
- ไม่เขียนใบงาน การมอบหมายงาน คาบเวลางาน หรือประวัติเอกสาร การปิด Staff ยังคงมีผลเดิมต่อรายการเลือกช่าง
- ปิดจากระบบอื่น: เมื่อ API ตรวจพบว่า inactive จะเพิ่ม version ถ้ายังตรงรุ่น token ที่ตรวจอยู่ รันซ้ำไม่เพิ่มไม่สิ้นสุด; ปิดแล้วเปิดกลับก่อนระบบตรวจพบยังไม่รับประกัน
- ลบอู่ทดลองใช้: เก็บ security tombstone และเพิ่มรุ่นในแถวเพิกถอนที่มีอยู่ของบัญชีที่กำลัง purge แทนการลบแถว ป้องกัน token ที่ถูกเพิกถอนไปแล้วกลับมาใช้ได้ในช่วงลบข้อมูล/ฐาน legacy ขัดข้อง
- นโยบายเวอร์ชันอยู่ใน `svc_AppUpdatePolicy` / `svc_AppUpdateAudit` ผ่าน `AppUpdatesDbContext` ที่ชี้ **ConnectionStrings:ServiceDb เท่านั้น** ไม่ใช้ฐานของ tenant ผู้แก้
- ไม่ลบ/ย้อนรุ่นข้อมูลเพิกถอนระหว่าง rollback; migration ปฏิเสธ `Down` เพื่อป้องกันการลบตารางโดยไม่ตั้งใจ

## Migration และ rollout

1. สำรองและตรวจฐานปลายทางก่อนรันคำสั่งจริง: dev เดิมอาจชี้ฐานร่วมกับ production
2. `ServiceDbContext` migration `AddUserSessionVersions` ต้องรันทั้ง GarageService และ GarageSelfServe ที่เปิดใช้งาน
3. `AppUpdatesDbContext` migration `InitialAppUpdatePolicies` รันครั้งเดียวใน GarageService ใช้ history `__AppUpdatesMigrationsHistory`
4. Docker image มี `efbundle-app-updates` เพิ่มเติม; deploy script รันก่อนเปลี่ยน API และไม่รัน context นี้ซ้ำบน self-serve
5. กำหนดช่วงหยุดเปลี่ยนสถานะ Staff ระหว่าง rollout หลาย instance จนทุก instance เป็นรุ่นใหม่ (instance เก่ายังไม่ตรวจ version)
6. ค่าเริ่มต้น policy ปิดทั้งหมด ไม่มีการบังคับอัปเดตจาก migration
7. ปล่อยแอปขึ้น Store → ตรวจว่าบัญชี/ประเทศเป้าหมายดาวน์โหลดได้ → เปิดแจ้งเตือนทั่วไป → ค่อยยกระดับขั้นต่ำ
8. rollback popup: ปิดนโยบายบนเว็บและให้แอปตรวจอีกครั้ง; ถ้า API ล่มและมี cache บังคับอยู่ แอปจะคงข้อกำหนดล่าสุดจนเชื่อมต่อสำเร็จ
9. ห้ามย้อน backend ไปก่อนรุ่นที่ตรวจ version หลังเริ่มเพิกถอนจริงแล้ว ให้แก้ไปข้างหน้าหรือใช้รุ่นที่ยังรองรับตารางนี้

หาก process ตายระหว่างเปลี่ยน Staff: SQL lock ถูกปล่อยเอง ธง `Changing` ที่ค้างหมดผลเองหลัง 120 วินาที (`SessionVersionStore.StaleChangingSeconds`) ปลอดภัยเพราะธงกันแค่การออก token ใหม่ รุ่นที่เพิ่มไปแล้วยังตัด token เดิม และ login ยังอ่านสถานะ Staff จากฐานเดิมอีกรอบ ให้ผู้ดูแลตรวจสถานะ Staff แล้วสั่งเปิด/ปิดซ้ำตามสถานะที่ต้องการผ่าน endpoint เดิม ห้ามลบแถว version เพื่อปลดล็อก

## การตรวจสอบ

- ชุด unit/middleware: หลาย instance ใช้ cache เก่า, token เดิม, pre-session, version-store ล่มเทียบกับ legacy ล่ม, auth ทุกเส้นทาง, สิทธิ์ Staff และ partial failure
- SQL integration: `SessionAndUpdatesSqlTests` ใช้ `GARAGEPRO_AUTH_SQL_CONNECTION` เฉพาะฐานชื่อ `GaragePro_Test_*` สร้าง/migrate ฐานทิ้งได้ ห้ามใส่ connection จริง; ตรวจ cross-connection fencing และ policy/audit CAS พร้อมกัน
- มือถือ: เปรียบเทียบตัวเลข, snooze, fallback cache, throttle/กลับจาก Store, Back/barrier, font ใหญ่, Store เปิดไม่ได้ และ response เก่าของบัญชีก่อน
- ก่อนเปิดจริงยังต้องทดสอบด้วยบัญชี staging หลายเครื่องและการติดตั้งจาก Store จริง ทั้ง iOS/Android
- log ใช้ shard/user ID/revision เท่านั้น ไม่ใส่ JWT; ติดตาม 401, 503, จำนวนการเพิกถอน และสถานะ HTTP ของ endpoint นโยบาย
- Meter `GaragePro.AccessUpdates`: `garagepro.sessions.revoked` (จำนวนบัญชีหลัง commit), `garagepro.sessions.store_errors` (operation), `garagepro.app_updates.reads` (success/error) สำหรับเชื่อมระบบ monitoring ของ deployment; ไม่ติด label เป็น token หรือ user ID

## ผลตรวจในเครื่อง 2026-10-28

- Backend: ผ่าน 1,688 รายการ รวม SQL integration บน SQL Server container แยกเฉพาะงานนี้; ข้าม 23 รายการเดิมที่ต้องใช้ฐาน/บริการเฉพาะของระบบเดิม
- SQL integration: migrate ทั้งสอง context, หลาย connection, lock เปิด/ปิดพร้อมกัน, legacy write ล้มหลังเพิกถอน, store ต่อไม่ได้ต้องไม่เปลี่ยน Staff, CAS policy/audit และการเพิกถอนจากระบบเดิมซ้ำ
- Flutter: 241 tests ผ่าน; `flutter analyze` ไม่มีปัญหา; Android debug APK และ iOS simulator build ผ่าน (ไม่ใช่ signed release สำหรับ Store)
- เว็บ: 95 tests และ production/prerender build ผ่าน; Playwright ทดสอบฟอร์มแยก platform, Store confirmation, ปิดนโยบาย, conflict/reload, สิทธิ์ผู้จัดการ, response เก่า 200/401, 503 ไม่ logout, heartbeat 60 วินาทีและ logout ข้ามสองแท็บ โดยใช้ API fixture ในเครื่อง
- หน้าเว็บหลังบ้านยังใช้ข้อจำกัดเดิมของ AppShell: เปิดใช้งานบนหน้าจอกว้างอย่างน้อย 1,024px
- เว็บสร้าง QueryClient และต้นไม้แอปใหม่ทุกครั้งที่ token เปลี่ยน (`lib/SessionGuard.tsx`) จึงวาง `<Toaster>` ไว้นอก provider นี้ ไม่งั้น toast ที่ยิงตอนสลับสาขาหายไป
- สร้าง migration bundle ของ `AppUpdatesDbContext` สำเร็จ โดยไม่ได้ใช้ bundle กับฐานจริง
- ยังไม่ได้ deploy, แก้ policy จริง, เผยแพร่ Store หรือทดสอบบัญชี staging บนอุปกรณ์จริง; ต้องทำขั้นเหล่านี้ตามลำดับ rollout ด้านบน

## รีวิวและแก้เพิ่ม 2026-10-29

- แก้: ตรวจสิทธิ์บัญชีผู้ดูแลทุกบัญชีก่อนเพิ่มรุ่น · ธง `Changing` หมดผลเอง 120 วินาที · 409 `STAFF_STATUS_BUSY` · error ฐานเดิมไม่ถูกห่อเป็น 503 · cache endpoint นโยบาย 30 วินาที · ย้าย Toaster ออกนอกต้นไม้ที่สร้างใหม่ · ปุ่มเปิด/ปิดพนักงานบอกเหตุผลเมื่อกดไม่ได้
- มือถือ: `pubspec.lock` และ iOS project ใน repo resolve ด้วย Flutter รุ่นใหม่กว่า 3.38 ที่เครื่อง dev ใช้ รัน `flutter test`/`pub get`/build iOS ด้วย 3.38 จะลดรุ่นแพ็กเกจใน lock และเปลี่ยน iOS project กลับเป็น CocoaPods อย่า commit ผลพลอยได้นี้ (รอบนี้เพิ่มเฉพาะ `url_launcher` เข้า lock เดิม)
- ตรวจแล้ว: backend 1,690 ผ่าน (+ SQL integration ผ่านบนฐานทิ้งได้ใน Docker) · เว็บ `tsc -b` + 95 tests · มือถือ `dart analyze` สะอาด + 241 tests
