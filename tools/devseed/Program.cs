using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

// ─────────────────────────────────────────────────────────────────────────────
// devseed — สร้างข้อมูลทดสอบให้ทั้งเว็บและแอป
//
// ทุกอย่างสร้างผ่าน API จริง ไม่ใช่ INSERT ตรงเข้า DB
// เพื่อให้กฎธุรกิจทุกข้อ (validate ก่อนส่ง · versioning · ลายเซ็นผูกเวอร์ชัน)
// ถูกบังคับเหมือนที่ผู้ใช้จริงจะเจอ — ข้อมูลที่ได้จึงเชื่อถือได้
//
// วิธีใช้:
//   cd tools/devseed
//   dotnet run                        # สาขา 105 · API localhost:5080
//   dotnet run -- --branch 30         # เลือกสาขาอื่น
//   dotnet run -- --api http://localhost:5081
//
// อ่าน connection string จาก user-secrets ตัวเดียวกับ API (UserSecretsId ตรงกัน)
// ─────────────────────────────────────────────────────────────────────────────

var api = ArgValue("--api") ?? "http://localhost:5080";
var branchId = int.Parse(ArgValue("--branch") ?? "105");

var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
var legacyCs = config["LegacyShards:ConnectionStrings:db2"];

if (string.IsNullOrWhiteSpace(legacyCs))
{
    Console.Error.WriteLine("ไม่พบ LegacyShards:ConnectionStrings:db2 ใน user-secrets");
    Console.Error.WriteLine("ตั้งค่าที่ backend/AMD.AutoService.GaragePro.API ด้วย dotnet user-secrets");
    return 1;
}

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.Add("X-Client-Source", "system");

Console.WriteLine($"API      {api}");
Console.WriteLine($"สาขา     {branchId}");
Console.WriteLine();

// ── 1. หาบัญชีพนักงานของสาขานี้แล้วเข้าสู่ระบบ ───────────────────────────────
var (userName, password) = await FindStaffLoginAsync(legacyCs, branchId);
if (userName is null)
{
    Console.Error.WriteLine($"ไม่พบบัญชีพนักงานที่ใช้เข้าระบบได้ในสาขา {branchId}");
    return 1;
}

Console.WriteLine($"เข้าสู่ระบบด้วย {userName} (ไม่แสดงรหัสผ่าน)");

var login = await PostAsync("/auth/login", new { userName, password });
if (login is null) return 1;

var loginUser = login.Value.GetProperty("user");
Console.WriteLine($"  {loginUser.GetProperty("displayName").GetString()} · " +
                  $"{loginUser.GetProperty("roleLabelTh").GetString()}");

SetToken(login.Value.GetProperty("accessToken").GetString()!);

// ── 2. เปิดกะ เพื่อให้ token ใช้เรียก API งานได้ ─────────────────────────────
var shifts = await GetAsync($"/auth/branches/{branchId}/shifts");
if (shifts is null) return 1;

var shiftId = shifts.Value[0].GetProperty("shiftId").GetGuid();
var session = await PostAsync("/auth/shift-sessions", new { branchId, shiftId });
if (session is null) return 1;

SetToken(session.Value.GetProperty("accessToken").GetString()!);
Console.WriteLine($"  เข้ากะ {session.Value.GetProperty("shiftName").GetString()} " +
                  $"ที่ {session.Value.GetProperty("branchName").GetString()}");
Console.WriteLine();

// ── 3. ตรวจแคตตาล็อกของสาขา ────────────────────────────────────────────────
var catalog = await GetAsync("/catalog");
var catalogCount = catalog?.GetArrayLength() ?? 0;

if (catalogCount == 0)
{
    Console.Error.WriteLine($"สาขา {branchId} ยังไม่มีแคตตาล็อก — เพิ่มบรรทัดในใบเสนอราคาไม่ได้");
    Console.Error.WriteLine("แก้ BranchId ใน backend/.../DevSeed.cs แล้วรีสตาร์ท API หรือใช้ --branch 105");
    return 1;
}

Console.WriteLine($"แคตตาล็อก {catalogCount} รายการ");

// ── 4. หางานว่างที่ยังไม่มีใบเสนอราคา ──────────────────────────────────────
var jobs = await FindOpenJobsAsync(legacyCs, branchId, take: 4);
if (jobs.Count < 4)
{
    Console.Error.WriteLine($"งานว่างในสาขานี้มีแค่ {jobs.Count} งาน ต้องการ 4 งาน");
    return 1;
}

Console.WriteLine($"งานว่าง {jobs.Count} งาน");
Console.WriteLine();

// ── 5. สร้างใบเสนอราคา 4 สถานะ ─────────────────────────────────────────────
var technicians = await GetAsync("/technicians");
var technicianId = technicians?.GetArrayLength() > 0
    ? technicians.Value[0].GetProperty("staffId").GetInt64()
    : (long?)null;

var results = new List<SeedResult>();

Console.WriteLine("สร้างข้อมูลทดสอบ");

// (ก) ฉบับร่าง — ใช้ทดสอบหน้าแก้ไขบนเว็บ (แก้บรรทัด เพิ่มลบ ส่งได้)
results.Add(await SeedDraftAsync(jobs[0], technicianId));

// (ข) ส่งให้ลูกค้าแล้ว — โผล่ในคิวของแอปมือถือ ใช้ทดสอบการอนุมัติ
results.Add(await SeedSentAsync(jobs[1], technicianId));

// (ค) อนุมัติบางส่วน + เซ็นแล้ว — ใช้ทดสอบหน้าเอกสารและลายเซ็น
results.Add(await SeedSignedAsync(jobs[2], technicianId));

// (ง) ถูกแทนที่ + ฉบับแก้ไข — ใช้ทดสอบ versioning
results.Add(await SeedRevisedAsync(jobs[3], technicianId));

// ── 6. สรุป ────────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("═══ ข้อมูลทดสอบที่สร้างแล้ว ═══");
Console.WriteLine();

foreach (var r in results.Where(r => r.QuotationId is not null))
{
    Console.WriteLine($"▸ {r.Label}");
    Console.WriteLine($"  {r.Code}  ·  {r.Vehicle}  ·  {r.CustomerName}");
    Console.WriteLine($"  เว็บ: {api.Replace(":5080", ":5173")}/quotations/{r.QuotationId}/{r.WebPage}");
    if (r.MobileNote is not null) Console.WriteLine($"  แอป: {r.MobileNote}");
    Console.WriteLine();
}

foreach (var r in results.Where(r => r.QuotationId is null))
    Console.WriteLine($"✕ {r.Label} — {r.Error}");

return 0;

// ─────────────────────────────────────────────────────── seed scenarios

async Task<SeedResult> SeedDraftAsync(JobRow job, long? techId)
{
    var q = await CreateWithLinesAsync(job, techId, deposit: 0m);
    if (q is null) return SeedResult.Failed("ฉบับร่าง (แก้ไขได้)", "สร้างไม่สำเร็จ");

    Console.WriteLine($"  ✓ ฉบับร่าง {q.Code}");
    return q with
    {
        Label = "ฉบับร่าง — ทดสอบหน้าแก้ไข 3 พาเนล",
        WebPage = "edit",
        MobileNote = "ยังไม่โผล่ในแอป (ต้องกดส่งให้ลูกค้าก่อน)"
    };
}

async Task<SeedResult> SeedSentAsync(JobRow job, long? techId)
{
    var q = await CreateWithLinesAsync(job, techId, deposit: 1000m);
    if (q is null) return SeedResult.Failed("รออนุมัติ", "สร้างไม่สำเร็จ");

    var sent = await PostAsync($"/quotations/{q.QuotationId}/send", new { });
    if (sent is null) return SeedResult.Failed("รออนุมัติ", "ส่งไม่สำเร็จ");

    Console.WriteLine($"  ✓ รออนุมัติ {q.Code}");
    return q with
    {
        Label = "รออนุมัติ — ทดสอบการอนุมัติบนแอป",
        WebPage = "document",
        MobileNote = "เปิดแอป → เห็นในคิว → กดเข้าไปอนุมัติรายบรรทัดและเซ็นได้"
    };
}

async Task<SeedResult> SeedSignedAsync(JobRow job, long? techId)
{
    var q = await CreateWithLinesAsync(job, techId, deposit: 500m);
    if (q is null) return SeedResult.Failed("อนุมัติบางส่วน", "สร้างไม่สำเร็จ");

    if (await PostAsync($"/quotations/{q.QuotationId}/send", new { }) is null)
        return SeedResult.Failed("อนุมัติบางส่วน", "ส่งไม่สำเร็จ");

    // ลูกค้าอนุมัติทุกบรรทัด ยกเว้นบรรทัดสุดท้าย — จะได้เห็นทั้งสองสถานะในเอกสาร
    var detail = await GetAsync($"/quotations/{q.QuotationId}");
    if (detail is null) return SeedResult.Failed("อนุมัติบางส่วน", "อ่านใบไม่สำเร็จ");

    var lines = detail.Value.GetProperty("lines").EnumerateArray().ToList();
    for (var i = 0; i < lines.Count; i++)
    {
        var lineId = lines[i].GetProperty("id").GetGuid();
        var isLast = i == lines.Count - 1;

        await PutAsync($"/quotations/{q.QuotationId}/lines/{lineId}/decision", isLast
            ? new { decision = "Rejected", rejectReason = "ขอทำครั้งหน้า" }
            : new { decision = "Approved", rejectReason = (string?)null });
    }

    // อัปโหลดลายเซ็นจริงแล้วเซ็น — เอกสารบนเว็บจะแสดงรูปนี้
    var signaturePath = await UploadSignatureAsync(job.JobId, q.QuotationId!.Value);
    if (signaturePath is null) return SeedResult.Failed("อนุมัติบางส่วน", "อัปโหลดลายเซ็นไม่สำเร็จ");

    var signed = await PostAsync($"/quotations/{q.QuotationId}/sign", new
    {
        signatureImagePath = signaturePath,
        consentText = "ข้าพเจ้าได้ตรวจสอบรายการซ่อมและราคาตามใบเสนอราคานี้แล้ว " +
                      "และยินยอมให้อู่ดำเนินการซ่อมเฉพาะรายการที่ข้าพเจ้าอนุมัติไว้",
        deviceInfo = "devseed · ข้อมูลทดสอบ",
        witnessEmployeeId = techId ?? 0,
        witnessEmployeeName = "พนักงานทดสอบ"
    });

    if (signed is null) return SeedResult.Failed("อนุมัติบางส่วน", "เซ็นไม่สำเร็จ");

    Console.WriteLine($"  ✓ อนุมัติบางส่วน + เซ็นแล้ว {q.Code}");
    return q with
    {
        Label = "อนุมัติบางส่วน + เซ็นแล้ว — ทดสอบเอกสารและลายเซ็น",
        WebPage = "document",
        MobileNote = "เปิดในแอปได้ แต่เป็นอ่านอย่างเดียว (เซ็นแล้ว)"
    };
}

async Task<SeedResult> SeedRevisedAsync(JobRow job, long? techId)
{
    var q = await CreateWithLinesAsync(job, techId, deposit: 0m);
    if (q is null) return SeedResult.Failed("ฉบับแก้ไข", "สร้างไม่สำเร็จ");

    if (await PostAsync($"/quotations/{q.QuotationId}/send", new { }) is null)
        return SeedResult.Failed("ฉบับแก้ไข", "ส่งไม่สำเร็จ");

    var revision = await PostAsync($"/quotations/{q.QuotationId}/revise", new
    {
        revisionReason = "ลูกค้าขอเปลี่ยนเป็นอะไหล่แท้ศูนย์ และเพิ่มตั้งศูนย์ถ่วงล้อ"
    });

    if (revision is null) return SeedResult.Failed("ฉบับแก้ไข", "ออกฉบับแก้ไขไม่สำเร็จ");

    var revisionId = revision.Value.GetProperty("id").GetGuid();
    var revisionCode = revision.Value.GetProperty("code").GetString()!;

    Console.WriteLine($"  ✓ ฉบับแก้ไข {revisionCode} (ฉบับเดิม {q.Code} → ถูกแทนที่)");

    return q with
    {
        QuotationId = revisionId,
        Code = $"{revisionCode} (ฉบับเดิม {q.Code} ถูกแทนที่)",
        Label = "ฉบับแก้ไข v2 — ทดสอบ versioning · การอนุมัติเดิมเป็นโมฆะ",
        WebPage = "edit",
        MobileNote = "ยังไม่โผล่ในแอป (ฉบับแก้ไขเป็นร่าง ต้องกดส่งก่อน)"
    };
}

// ─────────────────────────────────────────────────────── helpers

async Task<SeedResult?> CreateWithLinesAsync(JobRow job, long? techId, decimal deposit)
{
    var created = await PostAsync("/quotations", new
    {
        jobId = job.JobId,
        depositAmount = deposit,
        validUntil = (DateTime?)null
    });

    if (created is null) return null;

    var quotationId = created.Value.GetProperty("id").GetGuid();
    var code = created.Value.GetProperty("code").GetString()!;

    // ชุดรายการที่สมจริง: ค่าแรง + อะไหล่ แยกกลุ่มลูกค้าขอ/ช่างแนะนำ
    // ค่าแรงต้องระบุช่าง ไม่งั้น validate ไม่ผ่านตอนส่ง
    var lines = new object[]
    {
        new { catalogCode = "L-PM40-01",  quantity = 1m, discountPercent = 0m,  promotion = 0, source = "Customer",   assignedTechnicianId = techId },
        new { catalogCode = "P-OIL-1001", quantity = 1m, discountPercent = 0m,  promotion = 0, source = "Customer",   assignedTechnicianId = (long?)null },
        new { catalogCode = "P-FLT-0501", quantity = 2m, discountPercent = 0m,  promotion = 0, source = "Customer",   assignedTechnicianId = (long?)null },
        new { catalogCode = "P-BRK-0421", quantity = 1m, discountPercent = 5m,  promotion = 0, source = "Technician", assignedTechnicianId = (long?)null },
        new { catalogCode = "L-BRK-01",   quantity = 1m, discountPercent = 0m,  promotion = 0, source = "Technician", assignedTechnicianId = techId }
    };

    foreach (var line in lines)
        await PostAsync($"/quotations/{quotationId}/lines", line);

    return new SeedResult(
        QuotationId: quotationId,
        Code: code,
        Vehicle: $"{job.CarNumber} {job.Model}".Trim(),
        CustomerName: string.IsNullOrWhiteSpace(job.CustomerName) ? "ไม่ระบุชื่อลูกค้า" : job.CustomerName,
        Label: "", WebPage: "edit", MobileNote: null, Error: null);
}

async Task<string?> UploadSignatureAsync(long jobId, Guid quotationId)
{
    // PNG 1×1 — พอสำหรับทดสอบว่าเส้นทางอัปโหลดและแสดงรูปทำงาน
    var png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    using var form = new MultipartFormDataContent();
    var file = new ByteArrayContent(png);
    file.Headers.ContentType = new MediaTypeHeaderValue("image/png");

    form.Add(file, "file", "signature.png");
    form.Add(new StringContent(jobId.ToString()), "jobId");
    form.Add(new StringContent("signature"), "kind");
    form.Add(new StringContent(quotationId.ToString()), "entityId");

    var response = await http.PostAsync($"{api}/api/v1/attachments", form);
    var body = await response.Content.ReadAsStringAsync();

    using var json = JsonDocument.Parse(body);
    if (!json.RootElement.GetProperty("success").GetBoolean())
    {
        Console.Error.WriteLine($"    อัปโหลดลายเซ็นไม่สำเร็จ: {Describe(json.RootElement)}");
        return null;
    }

    return json.RootElement.GetProperty("data").GetProperty("relativePath").GetString();
}

async Task<(string? UserName, string Password)> FindStaffLoginAsync(string cs, int branch)
{
    await using var db = new SqlConnection(cs);
    await db.OpenAsync();

    var cmd = db.CreateCommand();
    cmd.CommandText = """
        SELECT TOP 1 u.UserName, u.Password
        FROM [User] u WITH (READUNCOMMITTED)
        JOIN Staff s WITH (READUNCOMMITTED) ON s.Id = u.StaffId
        WHERE s.BranchId = @branch
          AND ISNULL(u.Status, 0) = 1
          AND ISNULL(u.IsStaff, 0) = 1
          AND LEN(ISNULL(u.Password, '')) > 0
        ORDER BY u.Id
        """;
    cmd.Parameters.AddWithValue("@branch", branch);

    await using var reader = await cmd.ExecuteReaderAsync();
    return await reader.ReadAsync()
        ? (reader.GetString(0), reader.GetString(1))
        : (null, string.Empty);
}

async Task<List<JobRow>> FindOpenJobsAsync(string cs, int branch, int take)
{
    await using var db = new SqlConnection(cs);
    await db.OpenAsync();

    // เอาเฉพาะงานที่มีทะเบียนและชื่อลูกค้า และยังไม่มีใบเสนอราคา
    // เพื่อให้ข้อมูลทดสอบดูเหมือนของจริง
    var cmd = db.CreateCommand();
    cmd.CommandText = """
        SELECT TOP (@take)
            p.Id, p.JobNo, car.CarNumber,
            LTRIM(RTRIM(ISNULL(bc.Name, N'') + N' ' + ISNULL(cm.Name, N''))) AS Model,
            LTRIM(RTRIM(ISNULL(c.FirstName, N'') + N' ' + ISNULL(c.LastName, N''))) AS CustomerName
        FROM PJCarPickUp p WITH (READUNCOMMITTED)
        JOIN Car      car WITH (READUNCOMMITTED) ON car.Id = p.CarId
        LEFT JOIN Customer c   WITH (READUNCOMMITTED) ON c.Id  = p.CustomerId
        LEFT JOIN CarModel cm  WITH (READUNCOMMITTED) ON cm.Id = car.CarModelId
        LEFT JOIN BrandCar bc  WITH (READUNCOMMITTED) ON bc.Id = car.BrandCarId
        LEFT JOIN GarageService.dbo.svc_Quotation q ON q.LegacyJobId = p.Id
        WHERE p.BranchId = @branch
          AND car.CarNumber IS NOT NULL
          AND LEN(LTRIM(RTRIM(ISNULL(c.FirstName, N'') + ISNULL(c.LastName, N'')))) > 0
          AND q.Id IS NULL
        ORDER BY p.CreatedDate DESC
        """;
    cmd.Parameters.AddWithValue("@take", take);
    cmd.Parameters.AddWithValue("@branch", branch);

    var rows = new List<JobRow>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        rows.Add(new JobRow(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4)));

    return rows;
}

void SetToken(string token) =>
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

async Task<JsonElement?> GetAsync(string path) => await SendAsync(() => http.GetAsync($"{api}/api/v1{path}"), path);

async Task<JsonElement?> PostAsync(string path, object body) =>
    await SendAsync(() => http.PostAsJsonAsync($"{api}/api/v1{path}", body), path);

async Task<JsonElement?> PutAsync(string path, object body) =>
    await SendAsync(() => http.PutAsJsonAsync($"{api}/api/v1{path}", body), path);

async Task<JsonElement?> SendAsync(Func<Task<HttpResponseMessage>> send, string path)
{
    try
    {
        var response = await send();
        var body = await response.Content.ReadAsStringAsync();

        using var json = JsonDocument.Parse(body);
        if (json.RootElement.GetProperty("success").GetBoolean())
            return json.RootElement.GetProperty("data").Clone();

        Console.Error.WriteLine($"    {path} → {Describe(json.RootElement)}");
        return null;
    }
    catch (HttpRequestException)
    {
        Console.Error.WriteLine($"    {path} → เชื่อมต่อ API ไม่ได้ ({api}) — API รันอยู่หรือเปล่า");
        return null;
    }
    catch (JsonException)
    {
        Console.Error.WriteLine($"    {path} → คำตอบไม่ใช่ JSON (อาจเป็น error หน้า server)");
        return null;
    }
}

static string Describe(JsonElement envelope)
{
    if (!envelope.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
        return "ไม่สำเร็จ (ไม่มีรายละเอียด)";

    var code = error.TryGetProperty("code", out var c) ? c.GetString() : "UNKNOWN";
    var message = error.TryGetProperty("messageTh", out var m) ? m.GetString() : null;
    return $"{code} · {message}";
}

static string? ArgValue(string name)
{
    var args = Environment.GetCommandLineArgs();
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i] == name) return args[i + 1];
    return null;
}

internal sealed record JobRow(long JobId, string JobNo, string CarNumber, string Model, string CustomerName);

internal sealed record SeedResult(
    Guid? QuotationId,
    string Code,
    string Vehicle,
    string CustomerName,
    string Label,
    string WebPage,
    string? MobileNote,
    string? Error)
{
    public static SeedResult Failed(string label, string error) =>
        new(null, "", "", "", label, "", null, error);
}
