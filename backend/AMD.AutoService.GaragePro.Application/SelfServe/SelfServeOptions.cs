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

    /// <summary>เวลาไทย (Asia/Bangkok) รูปแบบ HH:mm — งานรายวันของ S4</summary>
    public sealed class JobsOptions
    {
        public string DailyAtLocalTime { get; set; } = "03:30";
        public string SummaryAtLocalTime { get; set; } = "08:30";
    }
}
