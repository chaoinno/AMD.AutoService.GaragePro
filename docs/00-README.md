# GaragePro Service Ops — Engineering Docs

เอกสารชุดนี้สกัดจาก design prototype ใน `GaragePro Service Ops Design/` (13 ไฟล์) เพื่อใช้เป็นฐานสร้าง 3 project:
**API (.NET 8)** · **Flutter app** · **React web**

| ไฟล์ | เนื้อหา | ใช้เมื่อ |
|---|---|---|
| **[10-handover.md](10-handover.md)** | **สถานะระบบล่าสุด · สิ่งที่ยังไม่ตัดสินใจ · งานถัดไป · กับดักที่เจ็บมาแล้ว** | **เริ่มที่นี่ถ้าเพิ่งเข้ามา** |
| [01-workflow.md](01-workflow.md) | State machine 10 สถานะ + 12 transition · happy path 13 ขั้น · sub-flow 10 โมดูล · roles/permissions · business rules · screen inventory 33 หน้า · design tokens · gaps · **9 open questions** | เข้าใจว่าระบบทำอะไร · เขียน guard · เขียน acceptance test |
| [02-domain-model.md](02-domain-model.md) | Aggregate map · enums ทุกตัว · entity + field ที่ prototype ใช้จริง · **15 invariant ที่ต้อง enforce ที่ API** | ออกแบบ schema · เขียน validator |
| [03-api-contract.md](03-api-contract.md) | Endpoint map ทุกหน้าจอ 15 หมวด · SignalR event · sync contract · cross-cutting | เขียน OpenAPI spec · แบ่งงาน client |
| [04-project-plan.md](04-project-plan.md) | Repo layout · stack 3 project + เหตุผล · shared contract · **10 phase vertical slice** · ความเสี่ยง · DoD | วางแผน sprint · ประเมินงาน |
| [06-customer-vehicle-management.md](06-customer-vehicle-management.md) | Customer/Vehicle CRUD · Garage mapping · privacy · API · validation · ผลทดสอบ | ดู contract และข้อจำกัดของโมดูลลูกค้า/รถ |

## ลำดับการอ่าน (แนะนำตาม Handoff doc ของ design)
1. เปิด prototype `GaragePro Demo.dc.html` → เข้าใจลำดับงานจริง 13 ขั้น
2. `01-workflow.md` → state machine + กฎธุรกิจ
3. `02-domain-model.md` → entity + invariant
4. `03-api-contract.md` → endpoint
5. `04-project-plan.md` → แผนงาน

## ⚠️ ก่อนเริ่มเขียนโค้ด
ต้องได้คำตอบ **Open Question 9 ข้อ** ใน [01-workflow.md §10](01-workflow.md#10-open-questions--9-ข้อที่ต้องได้คำตอบก่อนเขียนโค้ด)
Blocker จริง 2 ข้อ: **#1 master data ลูกค้า/รถ** และ **#3 นโยบาย offline conflict**

Design ระบุชัดว่า: *"ห้ามตีความจากหน้าจอเพียงอย่างเดียว เพราะต้นแบบเลือกทางที่เดินเรื่องได้ ไม่ใช่ทางที่องค์กรอนุมัติแล้ว"*
