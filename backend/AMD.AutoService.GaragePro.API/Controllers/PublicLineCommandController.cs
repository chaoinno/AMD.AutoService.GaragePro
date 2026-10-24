using System.Text.Json;
using AMD.AutoService.GaragePro.API.Internal;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// คำสั่ง <c>/SP_…</c> จากปุ่มบนการ์ดแจ้งชำระที่ webhook ของระบบเดิมส่งต่อมา (docs/17 §8.7.2 · ตัวส่งต่อเรียกว่า LineRelay)
/// · ไม่ใช้ JWT · ยืนยันตัวด้วย header <see cref="KeyHeader"/> เทียบแบบ constant-time กับ <c>SelfServe:Package:LineRelayKey</c>
///   ไม่ได้ตั้งคีย์ (หรือสั้นกว่า 32) = 404 เปล่าเหมือนไม่มี endpoint · ไม่มี/ผิดคีย์ = 401 เปล่า
/// · body อ่านเอง (ไม่ผ่าน model binding) · สัญญาตายตัวกับทีมระบบเดิม (docs/03 §16): ทุกกรณีที่ผ่านคีย์ตอบ 200
///   <c>{ "handled": bool, "messages": [LINE message objects ≤ 5] }</c> ตรงๆ (ไม่ห่อ envelope) ให้เอา messages ไปตอบกลุ่มได้ทันที
/// · [BIZ] ไม่ตอบ 500 ให้ตัวส่งต่อ: body เสีย/ระบบขัดข้อง = 200 handled:true พร้อมข้อความ "ขออภัย ระบบ ServicePro ขัดข้อง …"
///   (ถังจำกัดอัตราเต็มเป็นข้อยกเว้นเดียว: 429 จาก rate limiter)
/// · [SECURITY] ไม่ log ข้อความคำสั่ง (มีลายเซ็นที่ใช้อนุมัติได้) และไม่ log คีย์ · ถังจำกัดอัตราของตัวเอง
/// </summary>
[ApiController]
[Route(RoutePath)]
[Produces("application/json")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class PublicLineCommandController(
    IServiceProvider services,
    IOptions<SelfServeOptions> options,
    ILogger<PublicLineCommandController> logger) : ControllerBase
{
    public const string RoutePath = "api/v1/public/line-commands";
    public const string KeyHeader = "X-Line-Relay-Key";

    /// <summary>ข้อความคำสั่ง ≤ 300 ตัว + ชื่อ/ไอดี · กันคำขอที่ส่ง body ใหญ่เกินเหตุ</summary>
    public const long MaxBodyBytes = 8 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Post(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        if (options.Value.Package.EffectiveLineRelayKey is not { } key) return NotFound();
        var provided = Request.Headers[KeyHeader];
        if (provided.Count != 1 || !InternalApi.KeyMatches(provided[0], key)) return Unauthorized();

        var request = await ReadBodyAsync(ct);
        if (request is null)
        {
            // [SECURITY] ไม่ log body (อาจมีคำสั่งที่ใช้อนุมัติได้)
            logger.LogWarning("คำสั่งจากตัวส่งต่อ LINE: body ไม่ใช่ JSON object ตามสัญญา");
            return Ok(PackageLineCommandService.Unavailable());
        }

        if (services.GetService<PackageLineCommandService>() is not { } service)
            return Ok(LineRelayResponse.NotHandled);

        try
        {
            return Ok(await service.HandleAsync(request, ct));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "คำสั่งแจ้งชำระจาก LINE ทำไม่สำเร็จ");
            return Ok(PackageLineCommandService.Unavailable());
        }
    }

    /// <summary>null = ไม่ใช่ JSON object · ช่องที่ไม่ใช่ข้อความถือว่าไม่ได้ส่ง (ตัวเลข/ค่าอื่นไม่มีความหมายกับคำสั่งนี้)</summary>
    private async Task<LineRelayRequest?> ReadBodyAsync(CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct);
            return Parse(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (BadHttpRequestException)
        {
            return null;
        }
    }

    public static LineRelayRequest? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        return new LineRelayRequest(Field(root, "text"), Field(root, "lineUserId"), Field(root, "displayName"),
            Field(root, "groupId"));

        static string? Field(JsonElement root, string name)
        {
            foreach (var p in root.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
            return null;
        }
    }
}
