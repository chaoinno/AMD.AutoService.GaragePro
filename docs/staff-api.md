# Staff management API

โมดูลนี้อ่านและเขียนตารางพนักงานเดิมใน `Database=Garage` โดยเลือก shard จาก JWT claim และจำกัดรายการด้วย `BranchId` ของ session ปัจจุบัน ทุก endpoint ใช้ `[Authorize]` และ envelope มาตรฐานของระบบ

## Endpoints

| Method | Path | รายละเอียด |
|---|---|---|
| `GET` | `/api/v1/staffs` | ค้นหาและแบ่งหน้าฝั่ง server; รองรับ `keyword`, `departmentId`, `sectorId`, `positionId`, `provinceId`, `amphureId`, `districtId`, `includeInactive`, `page`, `pageSize` |
| `GET` | `/api/v1/staffs/{id}` | รายละเอียดพนักงาน บัญชีผู้ใช้ และทุกแผนกที่ผูกอยู่ |
| `GET` | `/api/v1/staffs/code-preview?branchId=` | ตัวอย่างรหัสพนักงาน/username/password ที่จะสร้าง |
| `POST` | `/api/v1/staffs` | สร้าง Staff + User + StaffSectorPosition + StaffRepairTypeNPoint ใน transaction เดียว (`multipart/form-data`) |
| `PUT` | `/api/v1/staffs/{id}` | แก้ไขข้อมูลและ diff-update แผนก (`multipart/form-data`) |
| `PATCH` | `/api/v1/staffs/{id}/status` | เปิดหรือปิดใช้งาน; เมื่อปิดต้องส่ง `endJobDate` |
| `GET` | `/api/v1/staffs/{id}/image` | เปิดรูปโดยตรวจ shard/branch/สิทธิ์ก่อน |
| `GET` | `/api/v1/staffs/reference-data` | สาขา เพศ ฝ่าย ตำแหน่ง และระดับฝีมือ |
| `GET` | `/api/v1/departments` | ฝ่ายที่ใช้งานอยู่ |
| `GET` | `/api/v1/sectors?departmentId=` | แผนกย่อยแบบ cascading; ไม่ส่ง `departmentId` จะได้ทุกแผนกพร้อมชื่อฝ่ายใน `secondary` |
| `GET` | `/api/v1/positions` | ตำแหน่งที่ใช้งานอยู่ |
| `GET` | `/api/v1/staff-skill-levels` | ระดับฝีมือที่ใช้งานอยู่ |

## Security and business behavior

- Staff ทั่วไปไม่เห็นพนักงานที่บัญชีมี `IsAdministrator=true`; Admin เห็นครบในสาขาปัจจุบัน
- เฉพาะ Admin แก้สาขา ฝ่าย แผนก และตำแหน่งของพนักงานเดิมได้ การตรวจนี้อยู่ที่ Application service ไม่ได้พึ่งการ disable control ฝั่ง Web
- รหัสใช้ `{yyMM}{BranchId อย่างน้อย 2 หลัก}{running 4 หลัก}` และล็อกการสร้างด้วย `sp_getapplock` ภายใน transaction
- ตอนสร้าง User ใช้ `Username = Password = Staff.Code` และเก็บ plaintext ตามการตัดสินใจของผู้ใช้วันที่ 2026-08-28
- รูปรองรับ JPG/JPEG/PNG ไม่เกิน 5 MB, แก้ EXIF orientation และ center-crop เป็น JPEG 300×300; รูปเก่าถูกลบหลัง transaction สำเร็จ
- การเปลี่ยน Staff และ StaffSectorPosition บันทึก `ChangeLog` โดยค่า location/branch/sector/position/skill แสดงเป็นชื่อ ไม่ใช่ raw ID
- ไม่มีหน้าแสดงประวัติการแก้ไขตามขอบเขตที่ผู้ใช้ยืนยัน แต่ audit ยังถูกบันทึก

## Verification checklist

- [ ] เรียก endpoint ทุกตัวโดยไม่มี token ได้ `401` envelope รหัส `AUTH_REQUIRED`
- [ ] Staff ทั่วไปค้นหาแล้วไม่พบผู้ใช้ Admin
- [ ] Admin เห็นผู้ใช้ Admin ในสาขาปัจจุบัน
- [ ] Staff ทั่วไปแก้ branch/department/sector/position ได้ `403 STAFF_ORGANIZATION_FORBIDDEN`
- [ ] สร้างพนักงานแล้ว Staff, User, assignment และ repair point เกิดครบ และ username/password เริ่มต้นตรงกับ code
- [ ] สร้างพร้อมกันในสาขาเดียวกันแล้ว code ไม่ซ้ำ
- [ ] ปิดใช้งานโดยไม่ส่งวันสิ้นสุดงานได้ `422 STAFF_END_DATE_REQUIRED`
- [ ] ปิดใช้งานแล้วรายการยังพบเมื่อ `includeInactive=true`; เปิดกลับแล้ว `EndJobDate` ถูกล้าง
- [ ] เปลี่ยนตำแหน่ง/แผนกแล้ว `ChangeLog` เก็บชื่อที่อ่านได้และ ID ของ assignment เดิมยังคงอยู่เมื่อ diff-update ได้
- [ ] อัปโหลดรูปผิดชนิด/เกินขนาดถูกปฏิเสธ; รูปที่รับได้มีขนาด 300×300 และรูปเก่าถูกลบ
