using System.Globalization;
using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.Internal;

/// <summary>
/// ตรวจสลิป/อนุมัติ/ปฏิเสธจาก GaragePro Admin (docs/17 §8.4 ระยะ B · §8.6) · ชั้นบางๆ หน้า <see cref="PackagePaymentAdminService"/>
/// ตัวเดียวกับ spadmin (กฎ/สูตรวัน/transaction/การแจ้งเจ้าของอู่อยู่ที่นั่นทั้งหมด ไม่ทำซ้ำที่นี่)
/// · ที่นี่ทำแค่: แปลงค่าจาก body/query · ตรวจผู้กระทำ · id ต้องเป็น GUID เต็ม (Admin มี id เต็มจากรายการเสมอ ไม่ใช้ prefix
///   แบบ spadmin เพื่อไม่ให้ไปโดนรายการอื่นที่ขึ้นต้นเหมือนกัน) · แปลงผลเป็น JSON ของ §8.6
/// · [SECURITY] ผู้กระทำ = <c>admin:{actorName}#{actorId}</c> จาก claim ของ admin ที่ Admin backend ตรวจ <c>[Authorize(Roles="Admin")]</c> แล้ว
///   (ServicePro เชื่อค่านี้เพราะคำขอผ่านคีย์ภายในมาแล้วเท่านั้น)
/// </summary>
public sealed class InternalPackagePaymentsService(
    PackagePaymentAdminService admin,
    IPackagePaymentRepository payments,
    IPackageSlipStorage slips)
{
    public const int DefaultTake = 50;
    public const int MaxActorNameLength = 100;
    public const string AllStatuses = "all";

    public async Task<Result<IReadOnlyList<InternalPackagePaymentDto>>> ListAsync(string? status, string? take,
        CancellationToken ct = default)
    {
        PackagePaymentStatus? filter = PackagePaymentStatus.Pending;
        var statusText = status?.Trim();
        if (!string.IsNullOrEmpty(statusText))
        {
            if (string.Equals(statusText, AllStatuses, StringComparison.OrdinalIgnoreCase)) filter = null;
            else if (PackagePaymentRules.TryParseStatus(statusText, out var parsed)) filter = parsed;
            else
                return Result<IReadOnlyList<InternalPackagePaymentDto>>.Fail(PackageErrors.Validation(
                    "status ต้องเป็น pending | approved | rejected | cancelled | all", "status"));
        }

        var count = DefaultTake;
        if (!string.IsNullOrWhiteSpace(take))
        {
            if (!int.TryParse(take.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 1)
                return Result<IReadOnlyList<InternalPackagePaymentDto>>.Fail(PackageErrors.Validation(
                    $"take ต้องเป็นจำนวนเต็ม 1 ถึง {PackagePaymentAdminService.MaxListTake}", "take"));
            count = Math.Min(count, PackagePaymentAdminService.MaxListTake);
        }

        var result = await admin.ListAsync(filter, null, count, ct);
        return result.Success
            ? Result<IReadOnlyList<InternalPackagePaymentDto>>.Ok(result.Data!.Select(InternalPackagePaymentMapper.ToDto).ToList())
            : Result<IReadOnlyList<InternalPackagePaymentDto>>.Fail(result.Error!);
    }

    /// <summary>ไฟล์สลิปของรายการ (ทุกสถานะ) · อู่ที่ลบแล้ว/ไม่พบไฟล์ = ไม่พบ · ผู้เรียกเป็นเจ้าของ stream</summary>
    public async Task<Result<PackageSlipFile>> OpenSlipAsync(string? id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var paymentId)) return Result<PackageSlipFile>.Fail(PackageErrors.NotFound());

        var entry = await payments.GetEntryAsync(paymentId, ct);
        if (entry is null || entry.Workspace.Status == WorkspaceStatus.Purged)
            return Result<PackageSlipFile>.Fail(PackageErrors.NotFound());

        var stream = await slips.OpenReadAsync(entry.Payment.SlipPath, ct);
        return stream is null
            ? Result<PackageSlipFile>.Fail(PackageErrors.NotFound())
            : Result<PackageSlipFile>.Ok(new PackageSlipFile(stream, entry.Payment.SlipContentType,
                PackagePaymentRules.SlipFileName(entry.Payment)));
    }

    /// <param name="request"><c>until</c> = วันสุดท้ายที่ใช้ได้ "yyyy-MM-dd" (ค่าเดียวกับ <c>spadmin approve-payment --until</c>)</param>
    public async Task<Result<InternalPackageReviewDto>> ApproveAsync(string? id, InternalReviewRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Guid.TryParse(id, out var paymentId)) return Fail(PackageErrors.NotFound());
        if (Actor(request) is not { } actor) return Fail(ActorError(request)!);
        if (!TryParseUntil(request.Until, out var until, out var untilError)) return Fail(untilError!);

        var result = await admin.ApproveAsync(paymentId.ToString("D"), until, request.Note, actor, ct);
        return Map(result);
    }

    public async Task<Result<InternalPackageReviewDto>> RejectAsync(string? id, InternalReviewRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Guid.TryParse(id, out var paymentId)) return Fail(PackageErrors.NotFound());
        if (Actor(request) is not { } actor) return Fail(ActorError(request)!);

        var result = await admin.RejectAsync(paymentId.ToString("D"), request.Reason, request.Note, actor, ct);
        return Map(result);
    }

    /// <summary>ผู้กระทำที่ลง sp_WorkspaceAudit/ReviewedBy · null = ค่าไม่ผ่าน (ดู <see cref="ActorError"/>)</summary>
    public static string? Actor(InternalReviewRequest request) =>
        ActorError(request) is null
            ? $"admin:{request.ActorName!.Trim()}#{long.Parse(request.ActorId!.Trim(), CultureInfo.InvariantCulture)}"
            : null;

    public static ApiError? ActorError(InternalReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = request.ActorName?.Trim();
        if (string.IsNullOrEmpty(name))
            return PackageErrors.Validation("ต้องระบุชื่อผู้ตรวจ (actorName)", "actorName");
        if (name.Length > MaxActorNameLength)
            return PackageErrors.Validation($"ชื่อผู้ตรวจยาวเกิน {MaxActorNameLength} ตัวอักษร", "actorName");
        if (name.Any(char.IsControl))
            return PackageErrors.Validation("ชื่อผู้ตรวจมีอักขระควบคุม (เช่น ขึ้นบรรทัดใหม่) ไม่ได้", "actorName");
        if (!long.TryParse(request.ActorId?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var actorId)
            || actorId <= 0)
            return PackageErrors.Validation("actorId ต้องเป็นเลขผู้ใช้ของ Admin ที่มากกว่า 0", "actorId");
        return null;
    }

    /// <summary>ว่าง = ต่อตามจำนวนเดือนที่แจ้ง · "yyyy-MM-dd" เท่านั้น · ปี ≥ 2400 = พิมพ์ปี พ.ศ. มา</summary>
    public static bool TryParseUntil(string? value, out DateOnly? until, out ApiError? error)
    {
        until = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var date))
        {
            error = PackageErrors.Validation("until ต้องเป็นวันที่รูปแบบ yyyy-MM-dd (ค.ศ.)", "until");
            return false;
        }

        if (date.Year >= 2400)
        {
            error = PackageErrors.Validation("until กรุณาใช้ปี ค.ศ.", "until");
            return false;
        }

        until = date;
        return true;
    }

    /// <summary>
    /// body JSON → <see cref="InternalReviewRequest"/> · ชื่อฟิลด์ไม่สนตัวพิมพ์ · ข้อความต้องเป็น string หรือ null ·
    /// actorId รับ number หรือ string ของตัวเลข · ผิดชนิด = 422 พร้อม field (ไม่ใช่ 400 ProblemDetails ของ framework)
    /// </summary>
    public static Result<InternalReviewRequest> ParseBody(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return Result<InternalReviewRequest>.Fail(BodyError());

        string? Text(string name, out ApiError? error)
        {
            error = null;
            if (!TryGet(root, name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            error = PackageErrors.Validation($"{name} ต้องเป็นข้อความ", name);
            return null;
        }

        var actorName = Text("actorName", out var e1);
        var until = Text("until", out var e2);
        var note = Text("note", out var e3);
        var reason = Text("reason", out var e4);
        if ((e1 ?? e2 ?? e3 ?? e4) is { } textError) return Result<InternalReviewRequest>.Fail(textError);

        string? actorId = null;
        if (TryGet(root, "actorId", out var id))
        {
            actorId = id.ValueKind switch
            {
                JsonValueKind.Number => id.GetRawText(),
                JsonValueKind.String => id.GetString(),
                JsonValueKind.Null => null,
                _ => "invalid"
            };
        }

        return Result<InternalReviewRequest>.Ok(new InternalReviewRequest(actorName, actorId, until, note, reason));
    }

    public static ApiError BodyError() => PackageErrors.Validation(
        "body ต้องเป็น JSON object เช่น { \"actorName\": \"…\", \"actorId\": 1 }", "body");

    private static bool TryGet(JsonElement root, string name, out JsonElement value)
    {
        if (root.TryGetProperty(name, out value)) return true;
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }

        value = default;
        return false;
    }

    private static Result<InternalPackageReviewDto> Map(Result<PackagePaymentReviewResult> result) =>
        result.Success
            ? Result<InternalPackageReviewDto>.Ok(InternalPackagePaymentMapper.ToDto(result.Data!))
            : Fail(result.Error!);

    private static Result<InternalPackageReviewDto> Fail(ApiError error) => Result<InternalPackageReviewDto>.Fail(error);
}
