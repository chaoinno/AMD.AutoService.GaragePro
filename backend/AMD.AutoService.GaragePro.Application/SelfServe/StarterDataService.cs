using System.Collections.Concurrent;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Attachments;
using AMD.AutoService.GaragePro.Application.Catalog;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Customers;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Jobs;
using AMD.AutoService.GaragePro.Application.Quotations;
using AMD.AutoService.GaragePro.Application.QuotationTemplates;
using AMD.AutoService.GaragePro.Application.Staff;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>
/// สร้าง/ล้างข้อมูลตัวอย่างของอู่ทดลองใช้ (S3) — เนื้อหาอยู่ที่ <see cref="StarterCatalog"/>
///
/// · [BIZ] สร้างผ่าน service จริงทั้งหมด (แคตตาล็อก · เทมเพลต · พนักงาน · ลูกค้า/รถ · จ๊อบ · ใบเสนอราคา) ในนามเจ้าของอู่
///   (Manager · <see cref="EventSource.Web"/>) — กฎธุรกิจ/ActivityEvent/ลิมิตแพ็กเกจเหมือนที่เจ้าของอู่กดเอง ไม่มีทางลัดเขียนฐานตรง
///   (ยกเว้นตั้งรหัส SAMPLE-… และการลบตอนล้าง ซึ่งไม่มี service ให้ใช้) · JobStateMachine ไม่ยอม transition จาก System
/// · ทุกขั้นทำใน <see cref="IShardScopeFactory.RunAsAsync{T}"/> — ผู้เรียก (signup ของ S1) เป็นคำขอแบบไม่ล็อกอิน
///   ICurrentUser ของคำขอนั้นจึงใช้ไม่ได้ และ ServiceDbContext ต้องเลือกฐาน sp1 ตั้งแต่ถูกสร้าง
/// · ทีละขั้น ล้มขั้นหนึ่งไม่หยุดขั้นอื่น (ขั้นที่พึ่งของที่ล้มจะข้ามพร้อมเหตุผล) · ทุกแถวที่สร้างลง ledger sp_SampleEntity ทันที
///   ผลรวม → sp_Workspace.SampleDataLoadedAt (สร้างได้อย่างน้อยหนึ่งแถว) / SampleDataError (สรุปขั้นที่ล้ม ไม่มีความลับ)
/// · [SECURITY] รหัสผ่านช่างตัวอย่างคืนผ่าน <see cref="StarterDataResult.Technicians"/> ครั้งเดียว ไม่ลง ledger/error/log
/// · สิทธิ์ "เจ้าของอู่เท่านั้น" เป็นหน้าที่ของผู้เรียก (S4 endpoint ตอบ <see cref="SelfServeErrors.SampleForbidden"/>) —
///   service นี้ถูกเรียกจาก signup/CLI ที่ไม่มีผู้ใช้ใน HTTP ด้วย จึงตรวจจาก ICurrentUser ของผู้เรียกไม่ได้
/// </summary>
public sealed class StarterDataService(
    IShardScopeFactory scopes,
    SelfServeOptions options,
    TimeProvider clock) : IStarterDataService
{
    public const string SystemActorName = "ระบบ (ข้อมูลตัวอย่าง)";

    /// <summary>เหตุผลของ transition ที่ยังไม่มีระบบตรวจอัตโนมัติ (WaitInspect→WaitQuote บังคับต้องมีเหตุผล)</summary>
    public const string SampleReason = "ข้อมูลตัวอย่าง";

    public const string ConsentText =
        "ข้อมูลตัวอย่าง: ลายเซ็นจำลองเพื่อสาธิตขั้นตอนลูกค้าอนุมัติ ไม่ใช่ลายเซ็นของลูกค้าจริง";

    /// <summary>ชนิดแถวใน ledger — ค่าคงที่ร่วมกับ ClearAsync และ S5 (conversion ข้ามแถวตัวอย่าง)</summary>
    public static class EntityTypes
    {
        public const string CatalogItem = "CatalogItem";
        public const string QuotationTemplate = "QuotationTemplate";
        public const string Staff = "Staff";
        public const string Customer = "Customer";
        public const string Car = "Car";
        public const string Job = "Job";
        public const string Quotation = "Quotation";
        public const string Attachment = "Attachment";
    }

    /// <summary>
    /// กันโหลด/ล้างซ้อนกันของอู่เดียวกันใน process เดียว (signup ยิง best-effort พร้อมกับที่เจ้าของอู่กดปุ่ม)
    /// · production มี API instance เดียว · หลาย instance ยังกันซ้ำได้บางส่วนด้วยรหัสสินค้า/เทมเพลตที่ unique ต่อสาขา
    /// · ไม่ลบ entry ทิ้ง (หนึ่ง semaphore ต่ออู่ — ขนาดเท่าจำนวนอู่ที่เคยกด ไม่โตไม่จำกัด)
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    private const int MaxErrorLength = 1900;

    public async Task<StarterDataResult> LoadAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var gate = Gates.GetOrAdd(workspaceId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var workspace = await ReadWorkspaceAsync(workspaceId, ct);
            if (workspace is null)
                return new StarterDataResult(false, [], StarterDataErrors.WorkspaceNotReady().MessageTh);

            // โหลดแล้ว = ไม่ทำซ้ำ (รหัสผ่านช่างคืนได้ครั้งเดียวตอนสร้าง — ครั้งนี้จึงไม่มีให้คืน)
            if (workspace.SampleDataLoadedAt is not null)
                return new StarterDataResult(true, [], null);

            return await scopes.RunAsAsync(OwnerOf(workspace), async (sp, token) =>
            {
                var store = sp.GetRequiredService<IStarterDataStore>();

                // ledger ค้างทั้งที่ยังไม่เคยโหลดสำเร็จ = รอบก่อนถูกตัดกลางทาง (process ตาย/timeout) —
                // ล้างของค้างก่อน ไม่งั้นรหัสสินค้า/เทมเพลตชนกันทั้งชุด และลูกค้าตัวอย่างซ้ำสองชุด
                var leftover = await store.GetLedgerAsync(workspaceId, token);
                if (leftover.Count > 0)
                {
                    var cleanup = new ClearRun(sp, store, workspace, options.ShardKey, Now);
                    await cleanup.ExecuteAsync(leftover, token);
                    if (cleanup.Errors.Count > 0)
                    {
                        var cleanupError = Summarize("ล้างข้อมูลตัวอย่างที่ค้างจากรอบก่อนไม่สำเร็จ", cleanup.Errors);
                        await store.SetSampleStateAsync(workspaceId, null, cleanupError, token);
                        return new StarterDataResult(false, [], cleanupError);
                    }
                }

                var run = new LoadRun(sp, store, workspace, options.ShardKey, Now);
                await run.ExecuteAsync(token);

                var error = run.Errors.Count == 0 ? null : Summarize("สร้างข้อมูลตัวอย่างได้ไม่ครบ", run.Errors);
                DateTime? loadedAt = run.CreatedCount > 0 ? Now : null;
                await store.SetSampleStateAsync(workspaceId, loadedAt, error, token);
                return new StarterDataResult(loadedAt is not null, run.Credentials, error);
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // [BIZ] best-effort: signup ต้องสำเร็จได้แม้ข้อมูลตัวอย่างพัง (ฐาน sp_* ล่ม ฯลฯ) — คืนผลแทนการโยน
            return new StarterDataResult(false, [], $"สร้างข้อมูลตัวอย่างไม่สำเร็จ ({Describe(ex)})");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Result<bool>> ClearAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var gate = Gates.GetOrAdd(workspaceId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var workspace = await ReadWorkspaceAsync(workspaceId, ct);
            if (workspace is null)
                return Result<bool>.Fail(StarterDataErrors.WorkspaceNotReady());

            return await scopes.RunAsAsync(OwnerOf(workspace), async (sp, token) =>
            {
                var store = sp.GetRequiredService<IStarterDataStore>();
                var ledger = await store.GetLedgerAsync(workspaceId, token);

                var run = new ClearRun(sp, store, workspace, options.ShardKey, Now);
                await run.ExecuteAsync(ledger, token);

                if (run.Errors.Count > 0)
                {
                    // ledger ของขั้นที่ล้มยังอยู่ → กดล้างซ้ำทำต่อจากที่ค้าง · คงสถานะ "โหลดแล้ว" เพราะยังมีของตัวอย่างเหลือ
                    var summary = Summarize("ล้างข้อมูลตัวอย่างได้ไม่ครบ", run.Errors);
                    await store.SetSampleStateAsync(workspaceId, workspace.SampleDataLoadedAt, summary, token);
                    return Result<bool>.Fail(StarterDataErrors.ClearIncomplete(summary));
                }

                // ล้างครบ → เปิดให้โหลดใหม่ได้ (SampleDataLoadedAt = null) และลบ error เดิมทิ้ง
                await store.SetSampleStateAsync(workspaceId, null, null, token);
                return Result<bool>.Ok(true);
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // ฐาน sp_* ล่มระหว่างอ่าน/เขียน ledger — ตอบเป็น envelope ปกติแทน HTTP 500 ดิบ (ledger ที่เหลือยังให้กดซ้ำได้)
            return Result<bool>.Fail(StarterDataErrors.ClearIncomplete(Describe(ex)));
        }
        finally
        {
            gate.Release();
        }
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>อ่าน workspace ใน scope ของตัวเอง — ไม่ใช้ DbContext ของผู้เรียก (signup อาจมีการเปลี่ยนแปลงค้างที่ยังไม่ save)</summary>
    private Task<StarterWorkspace?> ReadWorkspaceAsync(Guid workspaceId, CancellationToken ct) =>
        scopes.RunAsAsync(
            new ScopedCurrentUser(options.ShardKey, 0, 0, SystemActorName),
            (sp, token) => sp.GetRequiredService<IStarterDataStore>().GetWorkspaceAsync(workspaceId, token),
            ct);

    private ScopedCurrentUser OwnerOf(StarterWorkspace workspace) => new(
        options.ShardKey, workspace.BranchId, workspace.OwnerUserId, workspace.OwnerDisplayName,
        UserRole.Manager, EventSource.Web, workspace.OwnerStaffId);

    private static string Summarize(string headline, IReadOnlyList<string> errors)
    {
        var text = $"{headline}: {string.Join(" · ", errors)}";
        return text.Length <= MaxErrorLength ? text : text[..MaxErrorLength] + "…";
    }

    /// <summary>ชนิด exception + ข้อความสั้น — ไม่ใส่ stack trace (ข้อความลงคอลัมน์ที่ทีมงาน/หน้าจอสถานะอ่าน)</summary>
    private static string Describe(Exception ex)
    {
        var message = ex.Message.ReplaceLineEndings(" ");
        if (message.Length > 160) message = message[..160] + "…";
        return $"{ex.GetType().Name}: {message}";
    }

    private static DateTime ThaiToday(DateTime nowUtc) => nowUtc.AddHours(7).Date;

    /// <summary>ขั้นที่ล้มด้วยเหตุที่รู้สาเหตุ (error ของ service หรือสรุปรายการที่ทำไม่สำเร็จ) — ข้อความใช้ต่อได้ตรงๆ
    /// ต่างจาก exception อื่นที่ถูกนับเป็น "ระบบขัดข้อง"</summary>
    private sealed class StepFailedException : Exception
    {
        public StepFailedException(string action, ApiError error) : base($"{action}: {error.MessageTh} ({error.Code})") =>
            Error = error;

        public StepFailedException(string message) : base(message) { }

        public ApiError? Error { get; }
    }

    private static T Require<T>(Result<T> result, string action) =>
        result.Success ? result.Data! : throw new StepFailedException(action, result.Error!);

    private abstract class RunBase(IServiceProvider sp, StarterWorkspace workspace, string shardKey, DateTime nowUtc)
    {
        protected IServiceProvider Services { get; } = sp;
        protected StarterWorkspace Workspace { get; } = workspace;
        protected string ShardKey { get; } = shardKey;
        protected DateTime NowUtc { get; } = nowUtc;

        public List<string> Errors { get; } = [];

        protected T Get<T>() where T : notnull => Services.GetRequiredService<T>();

        protected async Task StepAsync(string label, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (StepFailedException ex)
            {
                Errors.Add($"{label}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Errors.Add($"{label}: ระบบขัดข้อง ({Describe(ex)})");
            }
        }
    }

    /// <summary>หนึ่งรอบของการสร้างข้อมูลตัวอย่าง — เก็บผลระหว่างขั้น (รหัสที่มีจริง · id ลูกค้า/รถ/ช่าง) ให้ขั้นถัดไปใช้</summary>
    private sealed class LoadRun(
        IServiceProvider sp, IStarterDataStore store, StarterWorkspace workspace, string shardKey, DateTime nowUtc)
        : RunBase(sp, workspace, shardKey, nowUtc)
    {
        private StarterBranchDefaults _defaults = StarterBranchDefaults.Empty;
        private readonly HashSet<string> _catalogCodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<long> _technicianStaffIds = [];
        private readonly long?[] _customerIds = new long?[StarterCatalog.Customers.Count];
        private readonly bool[] _customerIsOurs = new bool[StarterCatalog.Customers.Count];
        private readonly long?[] _vehicleIds = new long?[StarterCatalog.Vehicles.Count];

        public List<SampleCredential> Credentials { get; } = [];
        public int CreatedCount { get; private set; }

        public async Task ExecuteAsync(CancellationToken ct)
        {
            await StepAsync("ข้อมูลหลักเริ่มต้นของสาขา", () => BranchDefaultsAsync(ct));
            await StepAsync("แคตตาล็อก", () => CatalogAsync(ct));
            await StepAsync("เทมเพลตใบเสนอราคา", () => TemplatesAsync(ct));
            await StepAsync("ช่างตัวอย่าง", () => TechniciansAsync(ct));
            await StepAsync("ลูกค้าตัวอย่าง", () => CustomersAsync(ct));
            await StepAsync("รถตัวอย่าง", () => VehiclesAsync(ct));
            await StepAsync("รหัสข้อมูลตัวอย่าง", () => TagAsync(ct));
            foreach (var job in StarterCatalog.Jobs)
                await StepAsync($"จ๊อบตัวอย่าง {job.Key}", () => JobAsync(job, ct));
        }

        private async Task RecordAsync(string entityType, string entityId, CancellationToken ct)
        {
            await store.RecordAsync(Workspace.Id, entityType, entityId, ct);
            CreatedCount++;
        }

        private async Task BranchDefaultsAsync(CancellationToken ct)
        {
            // B2 seed หมวดหมู่ SVC/PART/FLUID + คลัง MAIN ให้สาขาที่ยังว่าง (idempotent — provisioning อาจทำไปแล้ว)
            await Get<IBranchDefaultsSeeder>().EnsureAsync(ShardKey, Workspace.BranchId, ct);
            _defaults = await Get<IStarterDataRepository>().GetBranchDefaultsAsync(ShardKey, Workspace.BranchId, ct);
        }

        private async Task CatalogAsync(CancellationToken ct)
        {
            var catalog = Get<ICatalogService>();
            var failed = new List<string>();

            foreach (var item in StarterCatalog.Items)
            {
                var request = new CatalogUpsertRequest(
                    item.Code, item.Type, item.Name, Compatibility: null, item.Unit, item.Cost, item.Price,
                    item.StandardHours, item.OnHand, Reserved: 0, OnOrder: 0, Damaged: 0, EtaNote: null,
                    CategoryId: _defaults.CategoryIdsByCode.TryGetValue(item.CategoryCode, out var categoryId) ? categoryId : null,
                    WarehouseId: item.Type == LineType.Part ? _defaults.WarehouseId : null);

                var result = await catalog.CreateAsync(request, ct);
                if (result.Success)
                {
                    await RecordAsync(EntityTypes.CatalogItem, result.Data!.Id.ToString(), ct);
                    _catalogCodes.Add(item.Code);
                }
                else if (result.Error!.Code == "CATALOG_CODE_DUPLICATE")
                {
                    // รหัสนี้มีในสาขาอยู่แล้ว (เจ้าของอู่สร้างเอง/ล้างแล้วเหลือไว้เพราะถูกใช้จริง) — ใช้ต่อได้ แต่ไม่ใช่ของตัวอย่าง
                    _catalogCodes.Add(item.Code);
                }
                else
                {
                    failed.Add($"{item.Code} {result.Error.MessageTh}");
                }
            }

            if (failed.Count > 0)
                Errors.Add($"แคตตาล็อก: สร้างไม่ได้ {failed.Count} รายการ ({string.Join(", ", failed.Take(3))})");
        }

        private async Task TemplatesAsync(CancellationToken ct)
        {
            var templates = Get<IQuotationTemplateService>();
            foreach (var template in StarterCatalog.Templates)
            {
                var missing = template.Lines.Where(l => !_catalogCodes.Contains(l.CatalogCode)).Select(l => l.CatalogCode).ToList();
                if (missing.Count > 0)
                {
                    Errors.Add($"เทมเพลต {template.Code}: ข้าม เพราะยังไม่มีรหัส {string.Join(", ", missing)}");
                    continue;
                }

                var lines = template.Lines.Select(l =>
                {
                    var item = StarterCatalog.Find(l.CatalogCode)!;
                    return new QuotationTemplateLineInput(item.Code, item.Name, item.Type, item.Unit, l.Quantity,
                        UnitPrice: null, UnitCost: null, StandardHours: null, DiscountPercent: 0m,
                        PromotionKind.None, LineSource.Customer, Note: null);
                }).ToList();

                var result = await templates.CreateAsync(
                    new QuotationTemplateUpsertRequest(template.Code, template.Name, template.Description, lines), ct);
                if (result.Success)
                    await RecordAsync(EntityTypes.QuotationTemplate, result.Data!.Id.ToString(), ct);
                else if (result.Error!.Code != "QUOTE_TEMPLATE_CODE_DUPLICATE") // มีอยู่แล้ว = ไม่ใช่ความผิดพลาด แต่ไม่ใช่ของตัวอย่าง
                    Errors.Add($"เทมเพลต {template.Code}: {result.Error.MessageTh} ({result.Error.Code})");
            }
        }

        private async Task TechniciansAsync(CancellationToken ct)
        {
            var staffs = Get<IStaffService>();
            foreach (var technician in StarterCatalog.Technicians)
            {
                // sp1 (B5): กรอกแค่ชื่อ/เบอร์/บทบาท — ระบบเติมเพศ/ตำแหน่ง/แผนกตามบทบาท ออกชื่อผู้ใช้ SP{no}.T{nn}
                // + รหัสผ่านสุ่ม (คืนครั้งเดียวใน Account.InitialPassword) และตั้ง svc_UserRoleOverride = Technician ให้เอง
                var request = new StaffUpsertRequest(
                    BranchId: 0, technician.FirstName, technician.LastName, GenderId: 0, IdCard: null,
                    Address1: null, Address2: null, ProvinceId: null, AmphureId: null, DistrictId: null, ZipCode: null,
                    PhoneNumber1: technician.Phone, PhoneNumber2: null, Email: null, LineId: null, Salary: 0m,
                    StaffSkillLevelId: null, ExperienceYear: 0, ExperienceMonth: 0, StartJobDate: null, EndJobDate: null,
                    Note: StarterCatalog.SampleMark, MainSectorId: 0, PositionId: 0, AdditionalSectorIds: null,
                    UserName: null, Password: null, Role: UserRole.Technician);

                var result = await staffs.CreateAsync(request, image: null, ct);
                if (!result.Success)
                {
                    Errors.Add($"ช่างตัวอย่าง: เพิ่ม{technician.FirstName}ไม่ได้ ({result.Error!.MessageTh}, {result.Error.Code})");
                    // ลิมิตพนักงานเต็มแล้ว คนถัดไปก็ไม่ผ่านเช่นกัน — ไม่ต้องลองต่อ
                    if (result.Error.Code == PlanErrors.LimitReachedCode) return;
                    continue;
                }

                var created = result.Data!;
                await RecordAsync(EntityTypes.Staff, created.Id.ToString(), ct);
                _technicianStaffIds.Add(created.Id);

                if (!string.IsNullOrEmpty(created.Account.InitialPassword))
                    Credentials.Add(new SampleCredential(created.Account.UserName, created.Account.InitialPassword));
            }
        }

        private async Task CustomersAsync(CancellationToken ct)
        {
            var customers = Get<ICustomerVehicleService>();
            var failed = new List<string>();

            for (var i = 0; i < StarterCatalog.Customers.Count; i++)
            {
                var sample = StarterCatalog.Customers[i];
                var result = await customers.CreateCustomerAsync(
                    new CustomerUpsertRequest(sample.FirstName, sample.LastName, sample.Phone), ct);

                if (result.Success)
                {
                    _customerIds[i] = result.Data!.Id;
                    _customerIsOurs[i] = true;
                    await RecordAsync(EntityTypes.Customer, result.Data.Id.ToString(), ct);
                }
                else if (result.Error!.Details is CustomerDuplicateDto duplicate)
                {
                    // ลูกค้าตัวอย่างชุดเดิมยังอยู่ (ล้างแล้วเหลือไว้เพราะมีจ๊อบจริงอ้างถึง) — ใช้คนเดิมต่อ ไม่นับเป็นของตัวอย่าง
                    _customerIds[i] = duplicate.Id;
                }
                else
                {
                    failed.Add($"{sample.FirstName} {result.Error.MessageTh}");
                }
            }

            if (failed.Count > 0)
                Errors.Add($"ลูกค้าตัวอย่าง: สร้างไม่ได้ ({string.Join(", ", failed)})");
        }

        private async Task VehiclesAsync(CancellationToken ct)
        {
            if (_customerIds.All(id => id is null))
            {
                Errors.Add("รถตัวอย่าง: ข้าม เพราะยังไม่มีลูกค้าตัวอย่าง");
                return;
            }

            var refs = await Get<IStarterDataRepository>().PickSampleVehicleRefsAsync(
                ShardKey, Workspace.ProvinceId, ThaiToday(NowUtc).Year - StarterCatalog.VehicleAgeYears,
                StarterCatalog.Vehicles.Count, ct);
            if (refs is null || refs.Models.Count == 0)
            {
                Errors.Add("รถตัวอย่าง: ข้ามรถและจ๊อบตัวอย่าง เพราะไม่พบข้อมูลยี่ห้อ/รุ่น/โฉม/ปีรถที่ใช้ได้");
                return;
            }

            var customerVehicles = Get<ICustomerVehicleService>();
            var failed = new List<string>();
            for (var i = 0; i < StarterCatalog.Vehicles.Count && i < _customerIds.Length; i++)
            {
                if (_customerIds[i] is not { } customerId) continue;

                var sample = StarterCatalog.Vehicles[i];
                var model = refs.Models[i % refs.Models.Count];
                var result = await customerVehicles.CreateVehicleAsync(new VehicleUpsertRequest(
                    customerId, sample.Registration, refs.ProvinceId,
                    model.BrandId, model.ModelId, model.NicknameId, refs.YearId), image: null, ct);
                if (!result.Success)
                {
                    failed.Add($"{sample.Registration} {result.Error!.MessageTh}");
                    continue;
                }

                _vehicleIds[i] = result.Data!.Id;
                await RecordAsync(EntityTypes.Car, result.Data.Id.ToString(), ct);
            }

            if (failed.Count > 0)
                Errors.Add($"รถตัวอย่าง: สร้างไม่ได้ ({string.Join(", ", failed)})");
        }

        private async Task TagAsync(CancellationToken ct)
        {
            var customers = new List<SampleRowTag>();
            var vehicles = new List<SampleRowTag>();
            for (var i = 0; i < _customerIds.Length; i++)
                if (_customerIsOurs[i] && _customerIds[i] is { } id)
                    customers.Add(new SampleRowTag(id, StarterCatalog.Customers[i].Code));
            for (var i = 0; i < _vehicleIds.Length; i++)
                if (_vehicleIds[i] is { } id)
                    vehicles.Add(new SampleRowTag(id, StarterCatalog.Vehicles[i].Code));

            if (customers.Count == 0 && vehicles.Count == 0) return;
            await Get<IStarterDataRepository>().TagSampleRowsAsync(ShardKey, customers, vehicles, ct);
        }

        private async Task JobAsync(StarterCatalog.Job sample, CancellationToken ct)
        {
            if (_customerIds.ElementAtOrDefault(sample.Index) is not { } customerId ||
                _vehicleIds.ElementAtOrDefault(sample.Index) is not { } vehicleId)
            {
                Errors.Add($"จ๊อบตัวอย่าง {sample.Key}: ข้าม เพราะไม่มีลูกค้า/รถตัวอย่าง");
                return;
            }

            // [BIZ] ค่าแรงต้องระบุช่างก่อนส่งใบเสนอราคา — ช่างตัวอย่างคนแรก ไม่มีก็ใช้เจ้าของอู่ (เป็นพนักงาน active ของสาขา)
            var technicianId = _technicianStaffIds.Count > 0 ? _technicianStaffIds[0] : Workspace.OwnerStaffId;
            var lines = sample.Lines.Where(l => _catalogCodes.Contains(l.CatalogCode)).ToList();
            if (lines.Count == 0)
            {
                Errors.Add($"จ๊อบตัวอย่าง {sample.Key}: ข้าม เพราะยังไม่มีรายการในแคตตาล็อก");
                return;
            }

            if (technicianId is null && lines.Any(l => StarterCatalog.Find(l.CatalogCode)?.Type == LineType.Labor))
            {
                Errors.Add($"จ๊อบตัวอย่าง {sample.Key}: ข้าม เพราะไม่มีช่างให้ระบุในบรรทัดค่าแรง");
                return;
            }

            var jobs = Get<IJobService>();
            var quotations = Get<IQuotationService>();

            var job = Require(await jobs.CreateAsync(new CreateJobRequest(
                customerId, vehicleId, JobTypeId: 9, SenderName: null, SenderPhoneNumber: null, sample.Detail,
                MileageAtIntake: sample.MileageAtIntake), ct), "เปิดจ๊อบ");
            await RecordAsync(EntityTypes.Job, job.JobId.ToString(), ct);

            // WaitInspect→WaitQuote ยังไม่มีระบบตรวจอัตโนมัติ (ตรวจเช็ค 31 รายการยังไม่มี) จึงต้องมีเหตุผลเสมอ
            Require(await jobs.TransitionAsync(job.JobId, new TransitionJobRequest("waitquote", SampleReason), ct),
                "เปลี่ยนสถานะเป็นรอเสนอราคา");

            var quotation = Require(await quotations.CreateAsync(new CreateQuotationRequest(job.JobId, null), ct),
                "สร้างใบเสนอราคา");
            await RecordAsync(EntityTypes.Quotation, quotation.Id.ToString(), ct);

            foreach (var line in lines)
            {
                var isLabor = StarterCatalog.Find(line.CatalogCode)?.Type == LineType.Labor;
                quotation = Require(await quotations.AddLineAsync(quotation.Id, new UpsertLineRequest(
                    line.CatalogCode, line.Quantity, UnitPrice: null, DiscountPercent: 0m, PromotionKind.None,
                    line.Source, isLabor ? technicianId : null, Note: null), ct),
                    $"เพิ่มรายการ {line.CatalogCode}");
            }

            quotation = Require(await quotations.SendAsync(quotation.Id, ct), "ส่งใบเสนอราคาให้ลูกค้า");
            Require(await jobs.TransitionAsync(job.JobId, new TransitionJobRequest("waitapprove", null), ct),
                "เปลี่ยนสถานะเป็นรอลูกค้าอนุมัติ");

            if (!sample.ApproveAndStart) return;

            // ลายเซ็นก่อนตัดสินใจรายบรรทัด: อัปโหลดไม่ได้ (FTP ไม่ได้ตั้งใน dev/ล่ม) จะหยุดที่ "รอลูกค้าอนุมัติ" แบบสะอาด
            // — ใบยังเป็น "ส่งแล้ว" ไม่มีการตัดสินใจค้างครึ่งๆ กลางๆ เจ้าของอู่ลองกดอนุมัติแทนลูกค้าเองได้
            var signature = await UploadSignatureAsync(job.JobId, quotation.Id, sample.Key, ct);
            if (signature is null) return;
            await RecordAsync(EntityTypes.Attachment, signature.Id.ToString(), ct);

            foreach (var line in quotation.Lines)
                quotation = Require(await quotations.DecideLineAsync(quotation.Id, line.Id,
                    new LineDecisionRequest(LineApprovalStatus.Approved, null), ct), $"อนุมัติรายการ {line.CatalogCode}");

            Require(await quotations.SignAsync(quotation.Id, new SignQuotationRequest(
                signature.RelativePath, ConsentText, $"ServicePro · {SampleReason}",
                Workspace.OwnerStaffId ?? 0, Workspace.OwnerDisplayName), ct), "เซ็นยืนยันใบเสนอราคา");

            Require(await jobs.TransitionAsync(job.JobId, new TransitionJobRequest("approved", null), ct),
                "เปลี่ยนสถานะเป็นอนุมัติแล้ว");
            Require(await jobs.TransitionAsync(job.JobId, new TransitionJobRequest("inprogress", null), ct),
                "เปลี่ยนสถานะเป็นกำลังซ่อม");
        }

        private async Task<AttachmentDto?> UploadSignatureAsync(Guid jobId, Guid quotationId, string jobKey,
            CancellationToken ct)
        {
            const string stopped = "จ๊อบหยุดที่ \"รอลูกค้าอนุมัติ\"";
            try
            {
                var png = Get<IStarterDataRepository>().GetSampleSignaturePng();
                await using var content = new MemoryStream(png, writable: false);
                var result = await Get<IAttachmentService>().UploadAsync(new UploadAttachmentRequest(
                    jobId, "signature", quotationId, "sample-signature.png", "image/png", content, png.Length), ct);
                if (result.Success) return result.Data;

                Errors.Add($"จ๊อบตัวอย่าง {jobKey}: อัปโหลดลายเซ็นตัวอย่างไม่สำเร็จ ({result.Error!.MessageTh}) · {stopped}");
                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Errors.Add($"จ๊อบตัวอย่าง {jobKey}: อัปโหลดลายเซ็นตัวอย่างไม่สำเร็จ ({Describe(ex)}) · {stopped}");
                return null;
            }
        }
    }

    /// <summary>
    /// ลบตาม ledger ย้อนลำดับการสร้าง: จ๊อบ (พร้อมใบเสนอราคา/ไฟล์แนบ/แถวลูกทั้งหมด) → รถ → ลูกค้า → ช่าง → เทมเพลต → แคตตาล็อก
    /// · แต่ละกลุ่มลบ ledger ของตัวเองเมื่อสำเร็จเท่านั้น — กลุ่มที่ล้มคง ledger ไว้ให้กดล้างซ้ำทำต่อได้ (idempotent)
    /// · ของที่ข้อมูลจริงอ้างอยู่ไม่ลบ: รถ/ลูกค้าที่มีจ๊อบจริง → ปล่อยไว้ · สินค้าที่ถูกใช้ในใบเสนอราคา/เทมเพลตจริง → ปิดใช้งาน
    ///   แล้วลบออกจาก ledger (นับเป็นข้อมูลจริงของอู่แล้ว) · ช่างตัวอย่าง → ปิดใช้งาน (ลบ [User]/Staff ไม่ได้ — ประวัติอ้างถึง)
    /// · หมวดหมู่/คลังเริ่มต้นของ B2 ไม่ใช่ข้อมูลตัวอย่าง ไม่แตะ
    /// </summary>
    private sealed class ClearRun(
        IServiceProvider sp, IStarterDataStore store, StarterWorkspace workspace, string shardKey, DateTime nowUtc)
        : RunBase(sp, workspace, shardKey, nowUtc)
    {
        private static readonly string[] JobFamily =
            [EntityTypes.Job, EntityTypes.Quotation, EntityTypes.Attachment];

        public async Task ExecuteAsync(IReadOnlyList<StarterLedgerEntry> ledger, CancellationToken ct)
        {
            // เรียงใหม่ที่นี่ ไม่พึ่งลำดับที่ store คืน — ภายในกลุ่มลบตัวที่สร้างทีหลังก่อน
            var byType = ledger.OrderByDescending(e => e.Id).ToLookup(e => e.EntityType);

            await StepAsync("ลบจ๊อบตัวอย่าง", () => JobsAsync(byType, ct));
            await StepAsync("ลบรถตัวอย่าง", () => EachAsync(byType[EntityTypes.Car], DeleteVehicleAsync, ct));
            await StepAsync("ลบลูกค้าตัวอย่าง", () => EachAsync(byType[EntityTypes.Customer], DeleteCustomerAsync, ct));
            await StepAsync("ปิดใช้งานช่างตัวอย่าง", () => EachAsync(byType[EntityTypes.Staff], DeactivateStaffAsync, ct));
            await StepAsync("ลบเทมเพลตตัวอย่าง", () => TemplatesAsync(byType[EntityTypes.QuotationTemplate].ToList(), ct));
            await StepAsync("ลบแคตตาล็อกตัวอย่าง", () => EachAsync(byType[EntityTypes.CatalogItem], DeleteCatalogItemAsync, ct));

            // ชนิดที่รุ่นนี้ไม่รู้จัก (ไม่ควรมี) — ไม่มีวิธีลบ จึงทิ้งแค่แถว ledger ไม่ให้ค้างทุกครั้งที่กดล้าง
            var known = new HashSet<string>([.. JobFamily, EntityTypes.Car, EntityTypes.Customer, EntityTypes.Staff,
                EntityTypes.QuotationTemplate, EntityTypes.CatalogItem]);
            var unknown = ledger.Where(e => !known.Contains(e.EntityType)).Select(e => e.Id).ToList();
            if (unknown.Count > 0)
                await StepAsync("ล้าง ledger", () => store.RemoveAsync(Workspace.Id, unknown, ct));
        }

        private async Task JobsAsync(ILookup<string, StarterLedgerEntry> byType, CancellationToken ct)
        {
            var family = JobFamily.SelectMany(t => byType[t]).ToList();
            if (family.Count == 0) return;

            var jobIds = byType[EntityTypes.Job].Select(e => ParseGuid(e.EntityId)).OfType<Guid>().ToList();
            // ใบเสนอราคา/ไฟล์แนบเป็นลูกของจ๊อบ — ลบไปพร้อมจ๊อบใน transaction เดียว ไม่มีทางลบแยก
            if (jobIds.Count > 0)
                await Get<IStarterDataRepository>().DeleteJobsAsync(ShardKey, Workspace.BranchId, jobIds, ct);
            await store.RemoveAsync(Workspace.Id, family.Select(e => e.Id).ToList(), ct);
        }

        private async Task EachAsync(IEnumerable<StarterLedgerEntry> entries,
            Func<StarterLedgerEntry, CancellationToken, Task<bool>> handle, CancellationToken ct)
        {
            var done = new List<long>();
            var failed = new List<string>();
            foreach (var entry in entries)
            {
                try
                {
                    if (await handle(entry, ct)) done.Add(entry.Id);
                    else failed.Add(entry.EntityId);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (StepFailedException ex)
                {
                    failed.Add($"{entry.EntityId} {ex.Error?.MessageTh ?? ex.Message}");
                }
                catch (Exception ex)
                {
                    failed.Add($"{entry.EntityId} {Describe(ex)}");
                }
            }

            if (done.Count > 0) await store.RemoveAsync(Workspace.Id, done, ct);
            if (failed.Count > 0)
                throw new StepFailedException($"ทำไม่สำเร็จ {failed.Count} รายการ ({string.Join(", ", failed.Take(3))})");
        }

        private async Task<bool> DeleteVehicleAsync(StarterLedgerEntry entry, CancellationToken ct)
        {
            if (!long.TryParse(entry.EntityId, out var id)) return true; // แถว ledger เสีย — ไม่มีอะไรให้ลบ
            // InUse (มีจ๊อบจริงอ้างถึง) = ปล่อยรถไว้เป็นข้อมูลจริงของอู่ แล้วเลิกติดตามใน ledger
            await Get<IStarterDataRepository>().DeleteVehicleAsync(ShardKey, Workspace.BranchId, id, ct);
            return true;
        }

        private async Task<bool> DeleteCustomerAsync(StarterLedgerEntry entry, CancellationToken ct)
        {
            if (!long.TryParse(entry.EntityId, out var id)) return true;
            await Get<IStarterDataRepository>().DeleteCustomerAsync(ShardKey, Workspace.BranchId, id, ct);
            return true;
        }

        private async Task<bool> DeactivateStaffAsync(StarterLedgerEntry entry, CancellationToken ct)
        {
            if (!long.TryParse(entry.EntityId, out var id)) return true;
            var result = await Get<IStaffService>().SetStatusAsync(id,
                new StaffStatusRequest(false, ThaiToday(NowUtc)), ct);
            if (result.Success || result.Error!.Code == "STAFF_NOT_FOUND") return true;
            throw new StepFailedException("ปิดใช้งาน", result.Error);
        }

        private async Task TemplatesAsync(IReadOnlyList<StarterLedgerEntry> entries, CancellationToken ct)
        {
            if (entries.Count == 0) return;
            var ids = entries.Select(e => ParseGuid(e.EntityId)).OfType<Guid>().ToList();
            if (ids.Count > 0)
                await Get<IStarterDataRepository>().DeleteQuotationTemplatesAsync(ShardKey, Workspace.BranchId, ids, ct);
            await store.RemoveAsync(Workspace.Id, entries.Select(e => e.Id).ToList(), ct);
        }

        private async Task<bool> DeleteCatalogItemAsync(StarterLedgerEntry entry, CancellationToken ct)
        {
            if (ParseGuid(entry.EntityId) is not { } id) return true;
            var outcome = await Get<IStarterDataRepository>().DeleteCatalogItemAsync(ShardKey, Workspace.BranchId, id, ct);
            if (outcome != SampleDeleteOutcome.InUse) return true;

            // ถูกใช้ในใบเสนอราคา/เทมเพลตจริงแล้ว — ลบไม่ได้ (เทมเพลตจริงจะพัง QUOTE_TEMPLATE_ITEM_MISSING) จึงปิดใช้งานแทน
            var result = await Get<ICatalogService>().SetStatusAsync(id, new CatalogStatusRequest(false), ct);
            if (result.Success || result.Error!.Code == "CATALOG_NOT_FOUND") return true;
            throw new StepFailedException("ปิดใช้งานสินค้า", result.Error);
        }

        private static Guid? ParseGuid(string value) => Guid.TryParse(value, out var id) ? id : null;
    }
}
