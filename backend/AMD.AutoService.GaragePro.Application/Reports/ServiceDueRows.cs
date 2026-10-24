using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Domain.StateMachine;

namespace AMD.AutoService.GaragePro.Application.Reports;

/// <summary>
/// กติกา "รถใกล้ครบรอบบริการ" ที่เดียว — ใช้ร่วมกันโดยรายงาน (<c>service-due</c>) ตัวเลขที่เมนู (<c>service-due/count</c>)
/// และงานแจ้งเตือนรายวัน เพื่อให้ตัวเลข/รายการ/แจ้งเตือนตรงกันเสมอ
/// · 1 แถวต่อรถ จากการส่งมอบล่าสุดของรถคันนั้น · วันนัดของแถว = รายการนัดที่ครบเร็วที่สุด (docs/03 §11)
/// · ตัดคันที่ (1) กลับมาเปิดจ๊อบใหม่หลังส่งมอบครั้งนั้น (ไม่นับที่ยกเลิก) หรือ (2) ยังมีจ๊อบอื่นที่ยังไม่ปิดอยู่ไม่ว่าเปิดเมื่อไหร่
///   (จ๊อบของใบส่งมอบนั้นเองไม่นับ — หลังเซ็นยังเป็น "พร้อมส่งมอบ" จนกว่าจะปิดงาน)
/// </summary>
public static class ServiceDueRows
{
    public static List<ServiceDueItemDto> Build(ServiceDueData data, DateOnly today)
    {
        var latestHandoverByVehicle = data.Handovers
            .GroupBy(h => h.VehicleId)
            .ToDictionary(g => g.Key, g => g.MaxBy(h => h.SubmittedAt)!.JobId);
        var visitsByVehicle = data.Visits.ToLookup(v => v.VehicleId);

        return data.Candidates
            .Where(h =>
            {
                var vehicleId = h.Job!.VehicleId;
                var isLatest = !latestHandoverByVehicle.TryGetValue(vehicleId, out var latestJobId) || latestJobId == h.JobId;
                var otherVisits = visitsByVehicle[vehicleId].Where(v => v.JobId != h.JobId).ToList();
                var cameBack = otherVisits.Any(v => v.CreatedAt > h.SubmittedAt && v.Status != JobStatus.Cancelled);
                var hasOpenJob = otherVisits.Any(v => !JobStateMachine.IsTerminal(v.Status));
                return isLatest && !cameBack && !hasOpenJob;
            })
            .Select(h =>
            {
                // 1 แถวต่อรถ — วันนัดของแถว = รายการที่ครบเร็วที่สุด · รายการทั้งหมดแสดงใต้แถว (เร็วสุดก่อน)
                var earliest = HandoverMapper.Earliest(h)!;
                var lines = h.ServiceItems
                    .OrderBy(i => i.NextServiceDueOn).ThenBy(i => i.SortOrder)
                    .Select(i => new ServiceDueLineDto(
                        i.Name, i.NextServiceMileage, i.NextServiceDueOn, i.NextServiceDueOn.DayNumber - today.DayNumber))
                    .ToList();
                return new ServiceDueItemDto(
                    h.Job!.VehicleId, h.Job.VehicleRegistration, h.Job.VehicleModel, h.Job.CustomerName, h.Job.CustomerPhone,
                    h.JobId, h.Job.JobNo, h.SubmittedAt!.Value, h.MileageAtHandover,
                    earliest.NextServiceMileage, earliest.NextServiceDueOn,
                    earliest.NextServiceDueOn.DayNumber - today.DayNumber, lines);
            })
            .OrderBy(x => x.NextServiceDueOn).ThenBy(x => x.VehicleRegistration)
            .ToList();
    }
}
