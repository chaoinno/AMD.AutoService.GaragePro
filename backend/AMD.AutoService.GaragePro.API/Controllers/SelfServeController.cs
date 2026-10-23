using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// อู่ที่สมัครทดลองใช้เอง (shard sp1) — สถานะทดลองใช้ · ขอต่ออายุ · ข้อมูลตัวอย่าง
/// · <c>[Authorize]</c> อย่างเดียว ไม่มี <c>[RequireShiftSession]</c> (ไม่ผูกกะ) · shard/สาขาจาก JWT
/// · ลูกค้าเดิม (db1/db2) และ deployment ที่ไม่ได้ตั้งฐาน self-serve ได้ 404 <c>SELFSERVE_NOT_APPLICABLE</c> เสมอ
///   (service ลงทะเบียนเฉพาะเมื่อตั้ง SelfServe:ConnectionString จึง resolve แบบ optional — ไม่ใช่ 500 ตอนสร้าง controller)
/// · เรียกได้แม้ทดลองใช้หมดอายุ: TrialReadOnlyFilter (B5) ข้าม prefix <c>/api/v1/selfserve</c> ทั้งหมด
/// </summary>
[ApiController]
[Route("api/v1/selfserve")]
[Produces("application/json")]
[Authorize]
public sealed class SelfServeController(IServiceProvider services) : ControllerBase
{
    /// <summary>สถานะทดลองใช้ + ลิมิตที่ใช้ไป + ตัวเลขเช็คลิสต์เริ่มต้น</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct) =>
        Service is { } s ? Render(await s.GetStatusAsync(ct)) : NotApplicable();

    /// <summary>ขอต่ออายุ/สนใจแพ็กเกจ — วันละครั้ง (ครั้งที่สองภายใน 24 ชม. = 409) · ส่ง LINE ทีมขาย</summary>
    [HttpPost("extension-request")]
    public async Task<IActionResult> RequestExtension([FromBody] ExtensionRequestInput? input, CancellationToken ct) =>
        Service is { } s
            ? Render(await s.RequestExtensionAsync(input ?? new ExtensionRequestInput(null, null), ct))
            : NotApplicable();

    /// <summary>ใส่ข้อมูลตัวอย่าง (เฉพาะผู้จัดการ · เมื่อยังไม่เคยใส่ หรือล้างไปแล้ว)</summary>
    [HttpPost("sample-data")]
    public async Task<IActionResult> LoadSampleData(CancellationToken ct) =>
        Service is { } s ? Render(await s.LoadSampleDataAsync(ct)) : NotApplicable();

    /// <summary>ล้างข้อมูลตัวอย่าง (เฉพาะผู้จัดการ)</summary>
    [HttpPost("sample-data/clear")]
    public async Task<IActionResult> ClearSampleData(CancellationToken ct) =>
        Service is { } s ? Render(await s.ClearSampleDataAsync(ct)) : NotApplicable();

    private ISelfServeStatusService? Service => services.GetService<ISelfServeStatusService>();

    private IActionResult NotApplicable() => Render(Result<object>.Fail(SelfServeErrors.NotApplicable()));

    private IActionResult Render<T>(Result<T> result) => result.Success
        ? Ok(Envelope.From(result, HttpContext.TraceIdentifier))
        : StatusCode(StatusFor(result.Error!.Code), Envelope.From(result, HttpContext.TraceIdentifier));

    /// <summary>
    /// HTTP status ของ error ที่ endpoint ชุดนี้คืนได้ — รหัสของ S3 (StarterDataErrors อยู่คนละ WP) ระบุด้วยข้อความ
    /// เพื่อไม่ต้องอ้างคลาสที่ยังไม่ได้ merge · ไม่รู้จัก = 422 แบบ controller อื่น
    /// </summary>
    public static int StatusFor(string? code) =>
        SelfServeErrors.StatusCodeFor(code)
        ?? Application.SelfServe.Package.PackageErrors.StatusCodeFor(code)
        ?? SelfServeLifecycleErrors.StatusCodeFor(code)
        ?? PlanErrors.StatusCodeFor(code)
        ?? code switch
        {
            "SELFSERVE_SAMPLE_WORKSPACE_NOT_READY" => StatusCodes.Status409Conflict,
            "SELFSERVE_SAMPLE_CLEAR_INCOMPLETE" => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status422UnprocessableEntity
        };
}
