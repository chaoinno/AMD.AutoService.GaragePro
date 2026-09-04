# 06 · Customer & Vehicle Management

โมดูลนี้ใช้ข้อมูลเดิมในฐาน `Garage` โดยตรง และไม่สร้างตารางลูกค้าหรือรถใหม่

## ขอบเขตข้อมูลเดิม

| ความหมาย | ตาราง Garage | คอลัมน์สำคัญ |
|---|---|---|
| ลูกค้า | `Customer` | `Id`, `Code`, ชื่อ/ที่อยู่/การติดต่อ, `IsBlacklist`, `Status`, `LastUserId` |
| รถ | `Car` | `Id`, `CarNumber`, `PorvindId`, master-data ids, `ImageUrl`, `Status` |
| เจ้าของรถ | `CarCustomer` | `CarId`, `CustomerId`, `Status`, `UpdatedBy` |

คำสั่งเขียนของโมดูลจำกัดอยู่ที่สามตารางนี้ การลบใช้ soft-delete (`Status=0`) และรถใหม่จะปรากฏทันทีจาก `CarCustomer` โดยไม่ต้องมี `PJCarPickUp` ก่อน

## สิทธิ์การเห็นข้อมูล

- ทุก endpoint ต้องมี JWT และ shift session
- ยึด shard และสาขาจาก JWT เท่านั้น ไม่ใช้ `Branch.IsCustomerDataPrivate` เพื่อขยายการเห็นข้อมูลข้ามสาขา
- เนื่องจาก `Customer`/`Car` เดิมไม่มี BranchId จึงใช้ผู้บันทึกผ่าน User → Staff, ผู้บันทึกความสัมพันธ์ CarCustomer ที่ยังใช้งาน หรือประวัติงาน PJCarPickUp ในสาขาปัจจุบัน
- รถต้องมีความสัมพันธ์กับสาขาด้วยตัวเอง การเห็นลูกค้าหนึ่งรายไม่ได้ทำให้เห็นรถอื่นของลูกค้ารายนั้นในสาขาอื่น
- กรองทั้งรายการ รายละเอียด เจ้าของรถ จำนวนรถ ตัวกรองยี่ห้อ/รุ่น ข้อมูลซ้ำ รูปภาพ CSV และการแก้ไข/ลบด้วย ID
- ลูกค้า/รถที่มีประวัติหลายสาขายังเป็นข้อมูล legacy รายการเดียว ไม่ใช่สำเนาแยกสาขา การแก้ไขรายการที่มีสิทธิ์จึงเปลี่ยนข้อมูลต้นฉบับร่วมกัน
- รายการขนาดใหญ่สร้างชุด id ที่มองเห็นได้ใน temporary table แล้วจึง paginate ที่ SQL Server เพื่อลด query timeout

## API

Base URL: `/api/v1` · response JSON ใช้ envelope มาตรฐานของระบบ

| Method | Endpoint | หมายเหตุ |
|---|---|---|
| GET | `/customers` | filter, sort, server-side pagination |
| GET | `/customers/{id}` | รายละเอียดพร้อมรถ |
| POST | `/customers` | เพิ่ม; ข้อมูลซ้ำคืน `409 CUSTOMER_DUPLICATE` |
| PUT | `/customers/{id}` | แก้ไข/เปิดใช้รายการที่เคย soft-delete |
| DELETE | `/customers/{id}` | soft-delete |
| GET | `/customers/export` | CSV UTF-8 BOM สูงสุด 10,000 รายการ |
| GET | `/vehicles` | ค้นทะเบียน/เจ้าของ/เบอร์/VIN/เลขเครื่อง พร้อม filter และ pagination |
| GET | `/vehicles/{id}` | รายละเอียดพร้อมเจ้าของ |
| POST | `/vehicles` | multipart; เพิ่มรถ เจ้าของ และรูปใน transaction |
| PUT | `/vehicles/{id}` | multipart; แก้ข้อมูล/เจ้าของ/รูป |
| DELETE | `/vehicles/{id}` | soft-delete |
| GET | `/vehicles/{id}/image` | ตรวจสิทธิ์ก่อนคืนไฟล์ |
| GET | `/vehicles/export` | CSV UTF-8 BOM สูงสุด 10,000 รายการ |
| GET | `/locations/provinces` | จังหวัด |
| GET | `/locations/amphures?provinceId=` | อำเภอแบบ cascading |
| GET | `/locations/districts?amphureId=` | ตำบลแบบ cascading |
| GET | `/locations/zipcode?districtId=` | รหัสไปรษณีย์ |
| GET | `/cars/reference-data` | master data รถทั้งหมดที่ใช้ในฟอร์ม |
| GET | `/cars/models?brandId=` | รุ่นตามยี่ห้อ |
| GET | `/cars/nicknames?modelId=` | โฉมตามรุ่น |

## กติกาสำคัญ

- ลูกค้าซ้ำตรวจจาก ชื่อ + นามสกุล + เบอร์โทรหลัก; UI ให้เลือก “อัปเดตข้อมูลเดิม” หรือ “ยกเลิก” เท่านั้น
- ลูกค้าติด blacklist ต้องมีเหตุผล และแสดง badge/tooltip ในรายการ
- รูปรถรองรับ JPG/JPEG/PNG ไม่เกิน 5 MB ตรวจทั้ง MIME, extension และ file signature
- รูปใหม่เก็บชื่อ `{vehicleId}_{timestamp}` แยกโฟลเดอร์ปี/เดือน และ path ใน DB เป็น relative path
- validation ทำทั้ง React และ API; error แสดงข้อความไทยพร้อม `traceId`

## การตรวจสอบ

- Backend Release build ผ่านด้วย .NET 9 SDK สำหรับ target `net8.0`
- Unit tests ผ่าน 53/53
- Frontend TypeScript + Vite production build ผ่าน
- Integration read กับ Garage สาขา private: ลูกค้า 5,877 รายประมาณ 0.7–0.9 วินาที, รถ 6,534 คันประมาณ 0.5–0.7 วินาที (page size 2)
- Swagger JSON ตรวจพบ endpoint ข้างต้นครบ
