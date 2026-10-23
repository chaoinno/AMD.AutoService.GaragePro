using AMD.AutoService.GaragePro.Application.Abstractions;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>
/// ค่าตั้งของ shard self-serve — section <c>SelfServe</c> (แผน §3) · ทุกตัวเลขเป็น config ไม่ hard-code (docs/12 §6)
/// [SECURITY] ConnectionString · Google:ClientId · Turnstile:SecretKey · Email:ApiKey · SalesLineGroupId
/// อยู่ใน dotnet user-secrets / env file เท่านั้น ห้าม commit (เหมือน Jwt:Key และ LineMessaging)
/// · <c>LegacyShards:ConnectionStrings:sp1</c> ต้องตั้งเป็นค่าเดียวกับ <see cref="ConnectionString"/> (Dapper 5 คลาสเปิดจากที่นั่น)
/// </summary>
public sealed class SelfServeOptions
{
    public const string SectionName = "SelfServe";
    public const string DefaultShardKey = "sp1";

    /// <summary>ปิด = signup ตอบ SIGNUP_DISABLED (503) · ใช้เป็นสวิตช์ rollback โดยไม่ต้อง deploy ใหม่ (แผน S6)</summary>
    public bool Enabled { get; set; }

    public string ShardKey { get; set; } = DefaultShardKey;

    /// <summary>ฐาน GarageSelfServe (svc_* + dbo.* รูปร่างเหมือน Garage + sp_*)</summary>
    public string ConnectionString { get; set; } = "";

    public TrialOptions Trial { get; set; } = new();
    public LimitOptions Limits { get; set; } = new();
    public GoogleOptions Google { get; set; } = new();
    public TurnstileOptions Turnstile { get; set; } = new();
    public EmailOptions Email { get; set; } = new();
    public BlocklistOptions Blocklist { get; set; } = new();
    public JobsOptions Jobs { get; set; } = new();
    public PackageOptions Package { get; set; } = new();

    /// <summary>ใช้สร้างลิงก์ในอีเมล/LINE — โดเมน canonical เดียว (docs/12 §8)</summary>
    public string PublicBaseUrl { get; set; } = "https://service.garage-pro.net";

    /// <summary>groupId LINE ของทีมขาย — ว่าง = ใช้ <c>LineMessaging:ContactGroupId</c> (กลุ่มฟอร์มติดต่อเดิม)</summary>
    public string SalesLineGroupId { get; set; } = "";

    public sealed class TrialOptions
    {
        public static readonly int[] DefaultReminderDays = [7, 1];

        /// <summary>อายุทดลองใช้ (วัน) นับจากวันสมัคร → EndPackageDate</summary>
        public int Days { get; set; } = 30;

        /// <summary>หลังหมดอายุยังอ่าน/พิมพ์ได้อีกกี่วันก่อนถูกลบ (docs/12 §6: 90)</summary>
        public int ReadOnlyDays { get; set; } = 90;

        /// <summary>
        /// ส่งอีเมลเตือนเมื่อเหลือกี่วัน — ว่าง = <see cref="DefaultReminderDays"/> (7, 1) · อ่านผ่าน <see cref="EffectiveReminderDays"/>
        /// ค่าเริ่มต้นไม่ใส่ไว้ที่ property โดยตั้งใจ: ConfigurationBinder "ต่อท้าย" array เดิมแทนการแทนที่
        /// ถ้า default เป็น [7,1] แล้ว config ส่ง [7,1] มาจะได้ [7,1,7,1] และส่งเตือนซ้ำโดยไม่มีอะไรฟ้อง
        /// </summary>
        public int[] ReminderDays { get; set; } = [];

        public IReadOnlyList<int> EffectiveReminderDays =>
            ReminderDays.Length > 0 ? ReminderDays : DefaultReminderDays;

        /// <summary>อีเมลชวนกลับมา (ครั้งเดียว) กี่วันหลังหมดอายุ</summary>
        public int ComebackDay { get; set; } = 45;
    }

    /// <summary>ลิมิตของแพ็กเกจทดลองใช้ (docs/12 §5) — [ASSUME] ค่าเริ่มต้นเสนอ ยังปรับได้</summary>
    public sealed class LimitOptions
    {
        public int Staff { get; set; } = 5;
        public int CatalogItems { get; set; } = 200;
        public int Customers { get; set; } = 200;
        public int Jobs { get; set; } = 100;

        /// <summary>ไฟล์แนบรวมต่ออู่บน FTP (MB) — ยังไม่มีตัวนับ ใช้เป็นเพดานอ้างอิงก่อน</summary>
        public int AttachmentMb { get; set; } = 200;

        public PlanLimits ToPlanLimits() => new(Staff, CatalogItems, Customers, Jobs);
    }

    public sealed class GoogleOptions
    {
        /// <summary>OAuth client id ของ Google Identity Services — ว่าง = ปิด Google sign-in/signup</summary>
        public string ClientId { get; set; } = "";
    }

    public sealed class TurnstileOptions
    {
        /// <summary>ว่าง = ข้ามการตรวจเฉพาะนอก Production</summary>
        public string SecretKey { get; set; } = "";

        /// <summary>site key สาธารณะของ widget — ส่งให้เว็บทาง GET /public/signup/config (ว่าง = เว็บไม่แสดง widget)</summary>
        public string SiteKey { get; set; } = "";
    }

    public sealed class EmailOptions
    {
        public const string LogProvider = "log";
        public const string CloudflareProvider = "cloudflare";

        /// <summary>
        /// <c>log</c> = เขียนลง log เท่านั้น (dev · IEmailSender.IsConfigured = false) ·
        /// <c>cloudflare</c> = Cloudflare Email Service (REST API · ต้องมี <see cref="AccountId"/> + <see cref="ApiKey"/> + <see cref="FromAddress"/>)
        /// · ค่าอื่นยังไม่รองรับ: resolve IEmailSender แล้วจะได้ error บอกชื่อ provider ไม่ fallback ไป log เงียบๆ
        /// </summary>
        public string Provider { get; set; } = LogProvider;

        public string FromAddress { get; set; } = "";
        public string FromName { get; set; } = "ServicePro";

        /// <summary>[SECURITY] Cloudflare: API token สิทธิ์ "Email Sending: Edit" — user-secrets / env file เท่านั้น</summary>
        public string ApiKey { get; set; } = "";

        /// <summary>Cloudflare account ID ที่ onboard โดเมนผู้ส่งไว้ (ไม่ใช่ความลับ แต่เก็บคู่กับ token)</summary>
        public string AccountId { get; set; } = "";

        /// <summary>ที่อยู่สำหรับลูกค้ากดตอบกลับ (ไม่บังคับ) — ว่าง = ตอบกลับไปที่ <see cref="FromAddress"/> ซึ่งมักไม่มีใครอ่าน</summary>
        public string ReplyToAddress { get; set; } = "";

        /// <summary>เปลี่ยนได้เฉพาะเทสต์/mock — production ใช้ค่าเริ่มต้นของ Cloudflare</summary>
        public string BaseUrl { get; set; } = "https://api.cloudflare.com/client/v4";

        public bool IsLogProvider =>
            string.IsNullOrWhiteSpace(Provider)
            || string.Equals(Provider, LogProvider, StringComparison.OrdinalIgnoreCase);

        public bool IsCloudflareProvider => string.Equals(Provider?.Trim(), CloudflareProvider, StringComparison.OrdinalIgnoreCase);
    }

    public sealed class BlocklistOptions
    {
        /// <summary>URL รายชื่อโดเมนอีเมลชั่วคราว (ว่าง = ใช้เฉพาะไฟล์ที่ฝังมากับ build + sp_BlockedDomain)</summary>
        public string Url { get; set; } = "";

        public int RefreshDays { get; set; } = 30;
    }

    /// <summary>
    /// แจ้งชำระเงิน/ต่ออายุแพ็กเกจ (docs/17 §5.5) · ค่าเริ่มต้นในโค้ดว่าง (ไม่มีราคา/บัญชี) · ค่าจริงของ production
    /// (ราคา 3,999/เดือน + VAT 7% · บัญชีกสิกรไทย · เจ้าของยืนยัน 2026-10-22) อยู่ใน appsettings.json ของ API และ spadmin
    /// · array ทุกตัวเริ่มว่างโดยตั้งใจ (ConfigurationBinder ต่อท้าย array เดิม · ดู <see cref="TrialOptions.ReminderDays"/>) อ่านผ่าน Effective...
    /// · [SECURITY] <see cref="SlipLinkKey"/> · <see cref="LineCommandKey"/> · <see cref="LineRelayKey"/> อยู่ใน user-secrets / env file เท่านั้น
    /// </summary>
    public sealed class PackageOptions
    {
        public static readonly int[] DefaultMonthOptions = [1, 2, 3, 6, 12];
        public static readonly int[] DefaultPaidReminderDays = [14, 3];

        /// <summary>จำนวนเดือนสูงสุดที่รับได้ต่อรายการ (กันพิมพ์ config ผิดเป็น 120)</summary>
        public const int MaxMonthCount = 36;

        /// <summary>[SECURITY] คีย์ HMAC ของลิงก์สลิปต้องยาวอย่างน้อยเท่านี้ ไม่งั้นถือว่าไม่ได้ตั้ง (ไม่ส่งรูปเข้า LINE)</summary>
        public const int MinSlipLinkKeyLength = 32;

        /// <summary>เวลาที่โอนเกินเวลาปัจจุบันได้ไม่เกินเท่านี้ (นาฬิกาเครื่องลูกค้าคลาด · วงล้อนาทีของแอปปัดขึ้นทีละ 5 นาที)</summary>
        public static readonly TimeSpan PaidAtFutureTolerance = TimeSpan.FromMinutes(5);

        /// <summary>ว่าง = <see cref="DefaultMonthOptions"/> · อ่านผ่าน <see cref="EffectiveMonthOptions"/></summary>
        public int[] MonthOptions { get; set; } = [];

        /// <summary>
        /// จำนวนเดือนที่ลูกค้าเลือกได้: ตัดซ้ำ · เฉพาะ 1..<see cref="MaxMonthCount"/> · เรียงน้อยไปมาก
        /// · config ผิดทั้งหมด (เหลือว่าง) ใช้ค่าเริ่มต้น ดีกว่าเปิดหน้าแล้วไม่มีตัวเลือกให้เลือก
        /// </summary>
        public IReadOnlyList<int> EffectiveMonthOptions
        {
            get
            {
                var valid = MonthOptions.Where(m => m is >= 1 and <= MaxMonthCount).Distinct().Order().ToArray();
                return valid.Length > 0 ? valid : DefaultMonthOptions;
            }
        }

        /// <summary>
        /// ราคาต่อเดือน**ก่อน VAT** (production 3,999 ใน appsettings.json) · ราคาของ N เดือน = N × ค่านี้ เว้นแต่ตั้ง <see cref="Prices"/>
        /// · ≤ 0 หรือไม่ตั้ง = ไม่มีราคามาตรฐาน · คำนวณที่ <c>Package.PackagePricing</c> ที่เดียว
        /// </summary>
        public decimal? MonthlyPrice { get; set; }

        /// <summary>
        /// อัตรา VAT เป็นสัดส่วน (0.07 = 7%) บวกเพิ่มจากราคาก่อน VAT · ไม่ตั้ง/0 = ไม่แยก VAT (ยอดโอน = ราคาก่อน VAT)
        /// · ต้องอยู่ช่วง 0 ถึงน้อยกว่า 1 · ผิดช่วง = ไม่แสดงราคาเลย (ดู <c>Package.PackagePricing</c>)
        /// </summary>
        public decimal? VatRate { get; set; }

        /// <summary>
        /// ราคาก่อน VAT ของจำนวนเดือนนั้นแทน N × <see cref="MonthlyPrice"/> (ไว้ทำส่วนลด) key = จำนวนเดือนเป็นข้อความ
        /// (<c>SelfServe:Package:Prices:12 = 43990</c>) · ≤ 0 = ไม่ได้ตั้ง (กลับไปใช้ MonthlyPrice)
        /// </summary>
        public Dictionary<string, decimal> Prices { get; set; } = new(StringComparer.Ordinal);

        public PaymentAccountOptions PaymentAccount { get; set; } = new();

        public string SlipLinkKey { get; set; } = "";

        /// <summary>คีย์ที่ใช้ได้จริง · null = ไม่ได้ตั้งหรือสั้นกว่า <see cref="MinSlipLinkKeyLength"/> ตัว</summary>
        public string? EffectiveSlipLinkKey =>
            SlipLinkKey?.Trim() is { Length: >= MinSlipLinkKeyLength } key ? key : null;

        /// <summary>อายุของลิงก์สลิปที่ส่งเข้า LINE (วัน)</summary>
        public int SlipLinkDays { get; set; } = 7;

        /// <summary>โดเมน API (ไม่ใช่โดเมนเว็บ) ที่ LINE ใช้ดึงรูปสลิป</summary>
        public string PublicApiBaseUrl { get; set; } = "https://gpservice-api.garage-pro.net";

        /// <summary>อีเมลเตือนอู่ paid ก่อนหมดอายุ (วัน) · ว่าง = <see cref="DefaultPaidReminderDays"/></summary>
        public int[] PaidReminderDays { get; set; } = [];

        public IReadOnlyList<int> EffectivePaidReminderDays =>
            PaidReminderDays.Length > 0 ? PaidReminderDays.Where(d => d > 0).Distinct().ToArray() : DefaultPaidReminderDays;

        /// <summary>วันที่โอนย้อนหลังได้ไม่เกินกี่วัน</summary>
        public int PaidAtMaxPastDays { get; set; } = 60;

        /// <summary>แจ้งชำระของสาขา GaragePro (db1/db2 กลุ่ม Service) ผ่านระบบแจ้งชำระเดิม (docs/17 §8.7.1)</summary>
        public GarageProApiOptions GarageProApi { get; set; } = new();

        /// <summary>
        /// [SECURITY] คีย์ HMAC ของคำสั่ง <c>/SP_…</c> บนปุ่มของการ์ด LINE (docs/17 §8.7.2) · ≥ 32 ตัว · user-secrets/env file เท่านั้น
        /// · ไม่ตั้ง = การ์ดไม่มีปุ่ม (อนุมัติผ่าน Admin/spadmin) และคำสั่งที่ส่งมาตรวจไม่ผ่านทุกครั้ง
        /// </summary>
        public string LineCommandKey { get; set; } = "";

        public string? EffectiveLineCommandKey =>
            LineCommandKey?.Trim() is { Length: >= MinSlipLinkKeyLength } key ? key : null;

        /// <summary>
        /// [SECURITY] คีย์ร่วมที่ตัวส่งต่อข้อความ LINE ส่งมาใน header <c>X-Line-Relay-Key</c> · ≥ 32 ตัว · user-secrets/env file เท่านั้น
        /// · ไม่ตั้ง = <c>POST /api/v1/public/line-commands</c> ตอบ 404
        /// </summary>
        public string LineRelayKey { get; set; } = "";

        public string? EffectiveLineRelayKey =>
            LineRelayKey?.Trim() is { Length: >= MinSlipLinkKeyLength } key ? key : null;

        /// <summary>
        /// LINE userId ที่กดอนุมัติ/ปฏิเสธผ่าน LINE ได้ · ว่าง = ทุกคนในกลุ่มทีมงาน (และคำสั่งที่ไม่มี groupId ถูกปฏิเสธ)
        /// · array เริ่มว่างโดยตั้งใจ (ConfigurationBinder ต่อท้าย array เดิม)
        /// </summary>
        public string[] LineApproverUserIds { get; set; } = [];

        public IReadOnlyList<string> EffectiveLineApproverUserIds =>
            LineApproverUserIds.Select(id => id?.Trim() ?? "").Where(id => id.Length > 0).Distinct(StringComparer.Ordinal)
                .ToArray();

        /// <summary>ลิงก์หน้าตรวจการแจ้งชำระใน GaragePro Admin (ไม่บังคับ) · ใส่ในคำตอบ LINE เมื่อทีมงานต้องไปอนุมัติที่นั่น</summary>
        public string AdminReviewUrl { get; set; } = "";

        public const string DefaultLineIconBaseUrl = "https://service.garage-pro.net/line-icons/";

        /// <summary>
        /// ที่อยู่ไอคอนบนการ์ด LINE (PNG ของ Material Design Icons · เก็บที่ <c>web/public/line-icons/</c> โครงโฟลเดอร์ตามแพ็กเกจต้นทาง) ·
        /// LINE ดึงรูปเองจึงต้องเป็น https ที่เปิดได้จากภายนอก · เครื่องทดสอบชี้ไป CDN ของแพ็กเกจเดียวกันได้ (ไฟล์ชุดเดียวกัน)
        /// </summary>
        public string LineIconBaseUrl { get; set; } = DefaultLineIconBaseUrl;

        public string EffectiveLineIconBaseUrl =>
            System.Uri.TryCreate(LineIconBaseUrl?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == System.Uri.UriSchemeHttps
                ? uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/"
                : DefaultLineIconBaseUrl;
    }

    /// <summary>
    /// API แจ้งชำระของระบบ GaragePro เดิม (docs/17 §8.7.1) · <see cref="BaseUrl"/>/<see cref="SlipBaseUrl"/> ไม่มีค่าเริ่มต้นในโค้ด
    /// · ว่าง = ปิด (หน้าแพ็กเกจของสาขา db2 บอกว่ายังแจ้งชำระผ่านระบบไม่ได้)
    /// </summary>
    public sealed class GarageProApiOptions
    {
        /// <summary>shard → zone ของ API เดิม (ไม่ใช่ความลับ) · ใช้เมื่อไม่ได้ตั้ง <see cref="Zones"/></summary>
        public static readonly IReadOnlyDictionary<string, int> DefaultZones =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["db1"] = 1, ["db2"] = 2 };

        public string BaseUrl { get; set; } = "";

        /// <summary>ต้นทางของรูปสลิปที่ระบบเดิมเก็บ (path ในตารางเป็น relative) · ว่าง = ไม่ส่งลิงก์สลิป</summary>
        public string SlipBaseUrl { get; set; } = "";

        /// <summary>ว่าง = <see cref="DefaultZones"/></summary>
        public Dictionary<string, int> Zones { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, int> EffectiveZones =>
            Zones.Count > 0 ? new Dictionary<string, int>(Zones, StringComparer.OrdinalIgnoreCase) : DefaultZones;

        public int? ZoneFor(string? shardKey) =>
            !string.IsNullOrWhiteSpace(shardKey) && EffectiveZones.TryGetValue(shardKey.Trim(), out var zone) ? zone : null;

        /// <summary>ลิงก์ http(s) แบบเต็ม · null = ไม่ได้ตั้งหรือรูปแบบผิด</summary>
        public Uri? EffectiveBaseUrl => Absolute(BaseUrl);

        public Uri? EffectiveSlipBaseUrl => Absolute(SlipBaseUrl);

        private static Uri? Absolute(string? value) =>
            Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? uri
                : null;
    }

    /// <summary>บัญชีรับเงินที่แสดงบนหน้าแจ้งชำระ (ไม่ใช่ความลับ แสดงให้ลูกค้าทุกคน · ค่า production อยู่ใน appsettings.json)</summary>
    public sealed class PaymentAccountOptions
    {
        public string BankName { get; set; } = "";
        public string AccountNo { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string PromptPayId { get; set; } = "";

        /// <summary>[BIZ] ต้องมีชื่อบัญชี + (เลขบัญชี หรือ PromptPay) ไม่งั้นแจ้งชำระไม่ได้ (ลูกค้าไม่รู้จะโอนไปไหน)</summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(AccountName)
            && (!string.IsNullOrWhiteSpace(AccountNo) || !string.IsNullOrWhiteSpace(PromptPayId));
    }

    /// <summary>เวลาไทย (Asia/Bangkok) รูปแบบ HH:mm — งานรายวันของ S4</summary>
    public sealed class JobsOptions
    {
        public string DailyAtLocalTime { get; set; } = "03:30";
        public string SummaryAtLocalTime { get; set; } = "08:30";
    }
}
