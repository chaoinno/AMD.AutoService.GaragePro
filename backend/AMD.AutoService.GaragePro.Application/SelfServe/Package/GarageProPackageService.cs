using System.Globalization;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

/// <summary>ผลการส่งที่จำไว้ต่อ requestId (10 นาที) · <see cref="Result"/> null = ส่งไปแล้วแต่ยังไม่ทราบผล</summary>
public sealed record GarageProSubmission(DateTime AttemptedAtUtc, PackagePaymentDto? Result);

/// <summary>
/// กันกดซ้ำของ flow garagepro (ไม่มีตารางของเรา) · Infrastructure: IMemoryCache + ล็อกต่อสาขาในหน่วยความจำ
/// · [RISK] ใช้ได้เพราะ API มี instance เดียว · ถ้าขยายเป็นหลาย instance ต้องเปลี่ยนเป็นล็อก/แคชกลาง
/// </summary>
public interface IGarageProSubmissionMemo
{
    /// <summary>ล็อกต่อสาขา (shard:branch) ตลอดการตรวจรายการรอตรวจ → ส่ง → จำผล</summary>
    Task<IDisposable> EnterAsync(string branchKey, CancellationToken ct = default);

    GarageProSubmission? Get(string requestKey);

    void Set(string requestKey, GarageProSubmission value);

    void Remove(string requestKey);
}

/// <summary>
/// แจ้งชำระของสาขา GaragePro กลุ่ม Service (db1/db2) ผ่านระบบแจ้งชำระเดิม (docs/17 §8.7.1 · D9)
/// · ServicePro ไม่เขียน Garage เอง: อ่าน Branch + ตารางแจ้งชำระแบบอ่านอย่างเดียว แล้วส่งต่อให้ API เดิมเป็นคนบันทึก/แจ้ง LINE/ต่อวัน
/// · shard ไม่อยู่ใน zone map หรือสาขาไม่ใช่กลุ่ม 7 = <c>SELFSERVE_NOT_APPLICABLE</c> (เหมือน shard อื่น)
/// · ไม่มียอดเงิน/หมายเหตุ (ตารางเดิมไม่มีช่อง) · ยกเลิกเองไม่ได้ · พรีวิววันหมดใช้สูตรของระบบเดิม = EndPackageDate + N เดือน
///   (ต่อจากวันหมดเดิมแม้หมดแล้ว ต่างจาก sp1 ที่นับจากวันอนุมัติ)
/// · กันกดซ้ำด้วย requestId ในหน่วยความจำ 10 นาที + ล็อกต่อสาขา + ตรวจรายการรอตรวจก่อนส่ง (กันที่ฝั่งเรา)
///   ส่งแล้วไม่ทราบผล (timeout) แล้วลองใหม่ด้วย requestId เดิม = ถ้ามีรายการรอตรวจที่เกิดหลังครั้งก่อน ถือว่าเป็นรายการเดียวกัน
/// </summary>
public sealed class GarageProPackageService(
    ICurrentUser user,
    IGarageProPackageReader reader,
    IGarageProPaymentGateway gateway,
    IPackageSlipNormalizer slips,
    IGarageProSubmissionMemo memo,
    SelfServeOptions options,
    TimeProvider clock) : IPackagePaymentService
{
    /// <summary>[BIZ] กลุ่ม Service (ค่าเดียวกับที่กั้น login ของ ServicePro)</summary>
    public const int ServiceBranchGroupId = 7;

    public const string LegacyPlan = "legacy";

    /// <summary>เวลาของเครื่องเราไม่ตรงกับฐานเดิมได้ · ใช้ตัดสินว่ารายการรอตรวจเกิดจากการส่งครั้งก่อนของ requestId เดียวกันไหม</summary>
    internal static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private bool IsManager => user.Role == UserRole.Manager;
    private SelfServeOptions.PackageOptions Package => options.Package;

    public async Task<Result<PackageOverviewDto>> GetOverviewAsync(CancellationToken ct = default)
    {
        var branch = await ResolveBranchAsync(ct);
        if (branch is null) return Result<PackageOverviewDto>.Fail(SelfServeErrors.NotApplicable());

        var now = Now;
        var endsAt = branch.EndsAtUtc;
        var pending = await reader.FindPendingAsync(user.ShardKey, branch.BranchId, ct);

        var monthOptions = Package.EffectiveMonthOptions.Select(months =>
        {
            var preview = PreviewEnd(endsAt, now, months);
            var price = PackagePricing.For(Package, months);
            return new PackageMonthOptionDto(months, price?.Subtotal, price?.Vat, price?.Total, preview,
                PackageRenewal.LastUsableDay(preview));
        }).ToList();

        var account = Package.PaymentAccount;
        var (code, reason) = CannotSubmit(pending is not null);

        return Result<PackageOverviewDto>.Ok(new PackageOverviewDto(
            branch.BranchId.ToString(CultureInfo.InvariantCulture),
            LegacyPlan,
            // [BIZ] ServicePro ไม่ล็อกสาขา db2 ตามวันหมด (D1) · expired แค่บอกสถานะ ไม่ทำให้ readOnly
            endsAt is { } end && now >= end ? "expired" : "active",
            endsAt,
            endsAt is { } e ? PackageRenewal.LastUsableDay(e) : null,
            PlanAccess.DaysLeft(endsAt, now),
            ReadOnly: false,
            PackagePricing.EffectiveVatRate(Package),
            monthOptions,
            account.IsConfigured
                ? new PackagePaymentAccountDto(Blank(account.BankName), Blank(account.AccountNo), Blank(account.AccountName),
                    Blank(account.PromptPayId))
                : null,
            pending is null ? null : ToDto(pending, IsManager, Package.GarageProApi.EffectiveSlipBaseUrl),
            code is null,
            code,
            reason,
            Package.PaidAtMaxPastDays,
            Flow: PackageFlows.GaragePro,
            CanCancel: false,
            AmountRequired: false));
    }

    public async Task<Result<IReadOnlyList<PackagePaymentDto>>> ListPaymentsAsync(int? take, CancellationToken ct = default)
    {
        var branch = await ResolveBranchAsync(ct);
        if (branch is null) return Result<IReadOnlyList<PackagePaymentDto>>.Fail(SelfServeErrors.NotApplicable());

        var rows = await reader.ListPaymentsAsync(user.ShardKey, branch.BranchId,
            Math.Clamp(take ?? PackagePaymentService.DefaultTake, 1, PackagePaymentService.MaxTake), ct);
        var slipBase = Package.GarageProApi.EffectiveSlipBaseUrl;
        return Result<IReadOnlyList<PackagePaymentDto>>.Ok(rows
            .Select(r => ToDto(r, IsManager, slipBase))
            .OfType<PackagePaymentDto>()
            .ToList());
    }

    public async Task<Result<PackagePaymentDto>> SubmitAsync(PackagePaymentSubmission submission, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var branch = await ResolveBranchAsync(ct);
        if (branch is null) return Fail(SelfServeErrors.NotApplicable());
        if (!IsManager) return Fail(PackageErrors.Forbidden());
        if (!Package.PaymentAccount.IsConfigured) return Fail(PackageErrors.Unavailable());
        var zone = Package.GarageProApi.ZoneFor(user.ShardKey);
        if (!gateway.IsConfigured || zone is null) return Fail(NotConnected());

        var requestId = PackagePaymentRules.ParseRequestId(submission.RequestId);
        if (!requestId.Ok) return Fail(requestId.Error!);

        var now = Now;
        var paidAt = PackagePaymentRules.ParsePaidAt(submission.PaidAt, now, Package.PaidAtMaxPastDays);
        if (!paidAt.Ok) return Fail(paidAt.Error!);
        var months = PackagePaymentRules.ParseMonthCount(submission.MonthCount, Package.EffectiveMonthOptions);
        if (!months.Ok) return Fail(months.Error!);
        // ยอดเงิน/หมายเหตุไม่ตรวจและไม่ส่ง (amountRequired: false) · ตารางเดิมไม่มีช่องเก็บ

        if (submission.Slip is not { } slip)
            return Fail(PackageErrors.Validation("กรุณาแนบสลิปการโอนเงิน 1 ไฟล์", "slip"));
        var check = slips.Validate(slip.ContentType, slip.SizeBytes);
        if (!check.IsValid) return Fail(SlipError(check.Code));

        var shard = user.ShardKey.Trim().ToLowerInvariant();
        var key = string.Create(CultureInfo.InvariantCulture, $"{shard}:{branch.BranchId}:{requestId.Value}");
        using var _ = await memo.EnterAsync(string.Create(CultureInfo.InvariantCulture, $"{shard}:{branch.BranchId}"), ct);

        var prior = memo.Get(key);
        if (prior?.Result is { } done) return Result<PackagePaymentDto>.Ok(done);

        // [BIZ] รอตรวจได้ครั้งละ 1 รายการ (กันที่ฝั่งเรา) · ยกเว้นเป็นผลของการส่งครั้งก่อนด้วย requestId นี้ที่ไม่ทราบผล
        var pending = await reader.FindPendingAsync(user.ShardKey, branch.BranchId, ct);
        if (pending is not null)
        {
            if (prior is not null && pending.CreatedAtUtc is { } created && created >= prior.AttemptedAtUtc - ClockSkew
                && ToDto(pending, IsManager, Package.GarageProApi.EffectiveSlipBaseUrl) is { } same)
            {
                var recovered = same with { MonthCount = months.Value };
                memo.Set(key, prior with { Result = recovered });
                return Result<PackagePaymentDto>.Ok(recovered);
            }

            return Fail(PackageErrors.PendingExists());
        }

        PreparedSlip prepared;
        try
        {
            prepared = await slips.NormalizeAsync(slip.Content, ct);
        }
        catch (InvalidDataException)
        {
            return Fail(PackageErrors.AttachmentTypeInvalid());
        }

        memo.Set(key, new GarageProSubmission(now, null));

        // ส่งแล้วต้องรอผลจนจบแม้ผู้ใช้ปิดหน้า (ยกเลิกกลางทาง = ไม่รู้ว่าระบบเดิมบันทึกหรือยัง) · gateway มี timeout ของตัวเอง
        var request = new GarageProAddPaymentRequest(zone.Value, branch.BranchId, BranchPackageDates.ToBranchLocal(paidAt.Value),
            months.Value, user.UserId, prepared.Content,
            string.Create(CultureInfo.InvariantCulture, $"slip-{branch.BranchId}-{now:yyyyMMddHHmmss}{prepared.Extension}"),
            prepared.ContentType);
        var result = await gateway.AddPaymentAsync(request, CancellationToken.None);

        switch (result.Outcome)
        {
            case GarageProAddPaymentOutcome.Accepted:
            {
                var dto = await ReadBackAsync(branch.BranchId, result.PaymentId) ??
                          Synthesized(result.PaymentId, requestId.Value, paidAt.Value, now);
                dto = dto with { MonthCount = months.Value };
                memo.Set(key, new GarageProSubmission(now, dto));
                return Result<PackagePaymentDto>.Ok(dto);
            }
            case GarageProAddPaymentOutcome.Rejected:
                // ระบบเดิมตอบชัดว่าไม่ได้บันทึก → ลองใหม่ด้วย requestId เดิมได้
                memo.Remove(key);
                return Fail(MapRejection(result.Message));
            default:
                // ไม่ทราบผล: คง memo ไว้ ลองใหม่ด้วย requestId เดิมจะจับคู่กับรายการรอตรวจที่อาจเกิดขึ้นแล้ว
                return Fail(PackageErrors.GarageProUnavailable());
        }
    }

    public async Task<Result<PackagePaymentDto>> CancelAsync(string? paymentId, CancellationToken ct = default)
    {
        var branch = await ResolveBranchAsync(ct);
        if (branch is null) return Fail(SelfServeErrors.NotApplicable());
        if (!IsManager) return Fail(PackageErrors.Forbidden());
        if (!TryParseId(paymentId, out var id)) return Fail(PackageErrors.NotFound());

        var row = await reader.GetPaymentAsync(user.ShardKey, branch.BranchId, id, ct);
        if (row is null) return Fail(PackageErrors.NotFound());
        return Fail(row.Status == 1 ? PackageErrors.NotCancellable() : PackageErrors.NotPending());
    }

    /// <summary>สลิปของระบบเดิมเปิดจาก <c>slip.url</c> ในรายการ · endpoint ดาวน์โหลดของเราไม่มีไฟล์ให้ (404)</summary>
    public async Task<Result<PackageSlipFile>> OpenSlipAsync(string? paymentId, CancellationToken ct = default)
    {
        var branch = await ResolveBranchAsync(ct);
        if (branch is null) return Result<PackageSlipFile>.Fail(SelfServeErrors.NotApplicable());
        if (!IsManager) return Result<PackageSlipFile>.Fail(PackageErrors.Forbidden());
        return Result<PackageSlipFile>.Fail(PackageErrors.NotFound());
    }

    public Task<PackageSlipFile?> OpenSignedSlipAsync(string? paymentId, string? exp, string? sig,
        CancellationToken ct = default) => Task.FromResult<PackageSlipFile?>(null);

    // ---------- กติกาล้วน (เทสต์ตรง) ----------

    /// <summary>
    /// สูตรของระบบเดิม: วันหมดใหม่ = EndPackageDate + N เดือนตามปฏิทินไทย (คงเวลาเดิม · 31 ม.ค. + 1 = 28/29 ก.พ.)
    /// · ไม่มีวันหมด = เที่ยงคืนไทยของวันนี้ + N เดือน
    /// </summary>
    public static DateTime PreviewEnd(DateTime? endsAtUtc, DateTime nowUtc, int months)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(months, 1);
        var baseLocal = endsAtUtc is { } end
            ? BranchPackageDates.ToBranchLocal(end)
            : BranchPackageDates.ToBranchLocal(nowUtc).Date;
        return BranchPackageDates.ToUtc(baseLocal.AddMonths(months));
    }

    /// <summary>1 รอตรวจ · 2 ยืนยันแล้ว · 0/-1 (ยกเลิก/ยกเลิกอัตโนมัติ) และค่าอื่น = ยกเลิก</summary>
    public static string StatusCode(int? status) => status switch
    {
        1 => "pending",
        2 => "approved",
        _ => "cancelled"
    };

    /// <summary>
    /// แถวของระบบเดิม → JSON ชุดเดียวกับ sp1 · id/shortId = เลขแถว · วันหมดใหม่เฉพาะที่ยืนยันแล้ว (ForEnd ของรายการรอตรวจเป็นค่าที่ระบบเดิม
    /// คำนวณไว้ตอนแจ้ง อาจไม่ตรงตอนยืนยัน) · ลิงก์สลิปเฉพาะผู้จัดการ · ไม่มีวันเวลาเลย = ข้ามแถว (null)
    /// </summary>
    public static PackagePaymentDto? ToDto(GarageProPaymentRow row, bool includeFinancials, Uri? slipBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(row);
        if ((row.PaidAtUtc ?? row.CreatedAtUtc) is not { } paidAt) return null;
        var submittedAt = row.CreatedAtUtc ?? paidAt;
        var status = StatusCode(row.Status);
        var approved = status == "approved";
        var id = row.Id.ToString(CultureInfo.InvariantCulture);
        var slipUrl = includeFinancials ? SlipUrl(slipBaseUrl, row.SlipPath) : null;

        return new PackagePaymentDto(
            id,
            id,
            status,
            MonthCount: null,
            Amount: null,
            ExpectedAmount: null,
            paidAt,
            submittedAt,
            string.IsNullOrWhiteSpace(row.CreatedByName) ? null : SelfServeText.SingleLine(row.CreatedByName, 100),
            Note: null,
            slipUrl is null ? null : new PackageSlipDto(null, null, slipUrl),
            PeriodStartsAt: null,
            PeriodEndsAt: approved ? row.ForEndUtc : null,
            PeriodLastUsableDate: approved && row.ForEndUtc is { } end ? PackageRenewal.LastUsableDay(end) : null,
            ReviewedAt: null,
            RejectReason: null,
            RejectReasonTh: null,
            RejectNote: null,
            CancelledAt: null);
    }

    /// <summary>
    /// ลิงก์รูปสลิป = SlipBaseUrl + path ที่ระบบเดิมเก็บ (escape ทีละ segment) · path ที่ไม่ใช่ relative หรือมี <c>..</c> = ไม่ส่งลิงก์
    /// </summary>
    public static string? SlipUrl(Uri? baseUrl, string? slipPath)
    {
        if (baseUrl is null || string.IsNullOrWhiteSpace(slipPath)) return null;
        var path = slipPath.Trim().Replace('\\', '/');
        if (path.Contains("://", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return null;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or "..")) return null;
        return baseUrl.AbsoluteUri.TrimEnd('/') + "/" + string.Join('/', segments.Select(Uri.EscapeDataString));
    }

    /// <summary>
    /// <c>Success: false</c> ของระบบเดิม: ข้อความไทยสั้นๆ ที่ไม่ใช่ข้อความทางเทคนิค = แสดงให้ลูกค้าเป็น 422 (เดา field จากคำในข้อความ)
    /// · อย่างอื่น (ว่าง · ภาษาอังกฤษ · exception) = 503 ข้อความกลางของเรา (ไม่เอาข้อความภายในของระบบอื่นไปแสดง)
    /// </summary>
    public static ApiError MapRejection(string? message)
    {
        var text = SelfServeText.SingleLine(message, 300);
        if (!LooksLikeUserMessage(text)) return PackageErrors.GarageProUnavailable();
        return new ApiError(PackageErrors.ValidationCode, text, FieldFor(text));
    }

    internal static bool LooksLikeUserMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 300) return false;
        if (!text.Any(c => c is >= '฀' and <= '๿')) return false;
        string[] technical = ["exception", "stack", "system.", "sql", "error", "object reference", "timeout", "null", " at "];
        return !technical.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FieldFor(string text) =>
        text.Contains("สลิป", StringComparison.Ordinal) || text.Contains("ไฟล์", StringComparison.Ordinal)
                                                         || text.Contains("รูป", StringComparison.Ordinal) ? "slip"
        : text.Contains("เดือน", StringComparison.Ordinal) ? "monthCount"
        : text.Contains("วัน", StringComparison.Ordinal) || text.Contains("เวลา", StringComparison.Ordinal) ? "paidAt"
        : null;

    // ---------- ตัวช่วย ----------

    private (string? Code, string? ReasonTh) CannotSubmit(bool hasPending)
    {
        if (!Package.PaymentAccount.IsConfigured) return ("unavailable", PackageErrors.Unavailable().MessageTh);
        if (!gateway.IsConfigured || Package.GarageProApi.ZoneFor(user.ShardKey) is null)
            return ("unavailable", NotConnected().MessageTh);
        if (!IsManager) return ("not_manager", "เฉพาะผู้จัดการอู่เท่านั้นที่แจ้งชำระเงินได้");
        if (hasPending) return ("pending_exists", PackageErrors.PendingExists().MessageTh);
        return (null, null);
    }

    private static ApiError NotConnected() => new(PackageErrors.UnavailableCode,
        "สาขานี้ยังแจ้งชำระผ่าน ServicePro ไม่ได้ (ยังไม่ได้เชื่อมต่อระบบแจ้งชำระของ GaragePro) " +
        $"กรุณาแจ้งชำระทางเดิม หรือติดต่อ {SelfServeText.ContactLine} / โทร {SelfServeText.ContactPhone}");

    private async Task<PackagePaymentDto?> ReadBackAsync(int branchId, long? paymentId)
    {
        try
        {
            var row = paymentId is { } id
                ? await reader.GetPaymentAsync(user.ShardKey, branchId, id, CancellationToken.None)
                : await reader.FindPendingAsync(user.ShardKey, branchId, CancellationToken.None);
            return row is null ? null : ToDto(row, IsManager, Package.GarageProApi.EffectiveSlipBaseUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // บันทึกที่ระบบเดิมแล้ว อ่านกลับไม่ได้ไม่ควรทำให้ผู้ใช้คิดว่าล้มเหลวแล้วส่งซ้ำ → สร้างจากค่าที่รู้
            return null;
        }
    }

    private static PackagePaymentDto Synthesized(long? paymentId, string requestId, DateTime paidAt, DateTime now)
    {
        var id = paymentId?.ToString(CultureInfo.InvariantCulture) ?? requestId;
        return new PackagePaymentDto(id, id, "pending", null, null, null, paidAt, now, null, null, null, null, null, null,
            null, null, null, null, null);
    }

    private async Task<GarageProBranchPackage?> ResolveBranchAsync(CancellationToken ct)
    {
        if (Package.GarageProApi.ZoneFor(user.ShardKey) is null) return null;
        var branch = await reader.GetBranchAsync(user.ShardKey, user.BranchId, ct);
        return branch is { BranchGroupId: ServiceBranchGroupId } ? branch : null;
    }

    private static bool TryParseId(string? value, out long id) =>
        long.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;

    private ApiError SlipError(string? code) => code switch
    {
        PackageErrors.AttachmentEmptyCode => PackageErrors.AttachmentEmpty(),
        PackageErrors.AttachmentTooLargeCode => PackageErrors.AttachmentTooLarge(slips.MaxSizeBytes),
        _ => PackageErrors.AttachmentTypeInvalid()
    };

    private static Result<PackagePaymentDto> Fail(ApiError error) => Result<PackagePaymentDto>.Fail(error);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// ตัวเลือก flow ของ endpoint ลูกค้า (docs/17 §8.7) ตาม shard ใน JWT: sp1 → <see cref="PackagePaymentService"/> ·
/// shard ที่อยู่ใน zone map (db1/db2) → <see cref="GarageProPackageService"/> (ซึ่งตรวจกลุ่ม 7 เอง) · อื่นๆ = <c>SELFSERVE_NOT_APPLICABLE</c>
/// · ลิงก์สลิปสาธารณะ (ไม่มีผู้ใช้) เป็นของ sp1 เท่านั้น
/// </summary>
public sealed class PackagePaymentRouter(
    ICurrentUser user,
    SelfServeOptions options,
    IPackagePaymentService? selfServe,
    IPackagePaymentService? garagePro) : IPackagePaymentService
{
    public IPackagePaymentService? Target =>
        SelfServeText.IsSelfServeShard(user.ShardKey, options) ? selfServe
        : options.Package.GarageProApi.ZoneFor(user.ShardKey) is not null ? garagePro
        : null;

    public Task<Result<PackageOverviewDto>> GetOverviewAsync(CancellationToken ct = default) =>
        Target?.GetOverviewAsync(ct) ?? Task.FromResult(Result<PackageOverviewDto>.Fail(SelfServeErrors.NotApplicable()));

    public Task<Result<IReadOnlyList<PackagePaymentDto>>> ListPaymentsAsync(int? take, CancellationToken ct = default) =>
        Target?.ListPaymentsAsync(take, ct)
        ?? Task.FromResult(Result<IReadOnlyList<PackagePaymentDto>>.Fail(SelfServeErrors.NotApplicable()));

    public Task<Result<PackagePaymentDto>> SubmitAsync(PackagePaymentSubmission submission, CancellationToken ct = default) =>
        Target?.SubmitAsync(submission, ct) ?? Task.FromResult(Result<PackagePaymentDto>.Fail(SelfServeErrors.NotApplicable()));

    public Task<Result<PackagePaymentDto>> CancelAsync(string? paymentId, CancellationToken ct = default) =>
        Target?.CancelAsync(paymentId, ct) ?? Task.FromResult(Result<PackagePaymentDto>.Fail(SelfServeErrors.NotApplicable()));

    public Task<Result<PackageSlipFile>> OpenSlipAsync(string? paymentId, CancellationToken ct = default) =>
        Target?.OpenSlipAsync(paymentId, ct) ?? Task.FromResult(Result<PackageSlipFile>.Fail(SelfServeErrors.NotApplicable()));

    public Task<PackageSlipFile?> OpenSignedSlipAsync(string? paymentId, string? exp, string? sig,
        CancellationToken ct = default) =>
        selfServe?.OpenSignedSlipAsync(paymentId, exp, sig, ct) ?? Task.FromResult<PackageSlipFile?>(null);
}
