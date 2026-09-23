using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AMD.AutoService.GaragePro.API;
using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.API.Controllers;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Infrastructure;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    // enum ส่งเป็น string เสมอ — token ต้องอ่านออกทั้ง Flutter และ React
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new()
    {
        Title = "GaragePro Service Ops API",
        Version = "v1",
        Description = "ระบบปฏิบัติการงานบริการ — ใบเสนอราคา เอกสาร และไฟล์แนบ"
    });

    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "ใส่ accessToken ที่ได้จาก /auth/login (หรือ /auth/shift-sessions สำหรับ Mobile flow เดิม)"
    });

    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });

    var xmlPath = Path.Combine(AppContext.BaseDirectory,
        $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xmlPath)) o.IncludeXmlComments(xmlPath);
});

// ---------- Auth ----------
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
    throw new InvalidOperationException(
        "ต้องตั้งค่า Jwt:Key ความยาวอย่างน้อย 32 ตัวอักษร (ใช้ dotnet user-secrets ห้าม commit)");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // 401 ต้องอยู่ในรูป envelope เดียวกับ error อื่น — client จะได้แสดง StateBlock ได้เหมือนกัน
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json; charset=utf-8";

                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    data = (object?)null,
                    error = new
                    {
                        code = "AUTH_REQUIRED",
                        messageTh = "เซสชันหมดอายุหรือยังไม่ได้เข้าสู่ระบบ — กรุณาเข้าสู่ระบบใหม่"
                    },
                    traceId = context.HttpContext.TraceIdentifier
                });
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ITokenIssuer, JwtTokenIssuer>();
builder.Services.AddGarageProInfrastructure(builder.Configuration);

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                 ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

// [SECURITY] API อยู่หลัง Nginx เสมอ (container bind 127.0.0.1 เท่านั้น) — ใช้ X-Forwarded-For ตัวขวาสุด
// ที่ Nginx เติมเอง (ForwardLimit = 1) เป็น IP จริงของผู้ใช้ ไม่งั้นทุกคำขอจะเป็น IP ของ docker gateway
// แล้ว rate limit ต่อ IP กลายเป็นเพดานรวมของทั้งโลก · ค่าที่ client ปลอมมาจะอยู่ทางซ้ายและถูกข้าม
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// [SECURITY] ฟอร์มติดต่อเป็น anonymous — ต่อ IP 5 ครั้ง/10 นาที + เพดานรวม 60 ครั้ง/ชั่วโมง
// กันคนยิงสแปมเข้ากลุ่ม LINE และกันโควตา push message ของ LINE OA หมด
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(PublicContactController.RateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.StartsWithSegments("/api/v1/public")
            ? RateLimitPartition.GetFixedWindowLimiter("public-global",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromHours(1), QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter("authenticated"));
    o.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            success = false,
            data = (object?)null,
            error = new
            {
                code = "RATE_LIMITED",
                messageTh = "ส่งข้อมูลถี่เกินไป กรุณารอสักครู่แล้วลองใหม่ หรือติดต่อทาง LINE @garagepro / โทร 090-996-6446"
            },
            traceId = context.HttpContext.TraceIdentifier
        }, ct);
    };
});

var app = builder.Build();

app.UseForwardedHeaders();

// สร้าง schema + seed แคตตาล็อกตัวอย่างในโหมด Development เท่านั้น
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // ต่อ DB ไม่ได้ก็ต้องยังสตาร์ทได้ — ทีม client ต้องดึง OpenAPI ไป codegen ได้แม้ยังไม่ได้ต่อ VPN
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();

        // ใช้ migration ไม่ใช่ EnsureCreated — EnsureCreated ไม่เพิ่มตารางให้ฐานที่มีอยู่แล้ว
        // ทำให้ตารางใหม่หายไปเงียบๆ จนกว่าจะมีคนเรียกใช้
        await db.Database.MigrateAsync();
        await DevSeed.RunAsync(db);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex,
            "เริ่มต้นฐานข้อมูลไม่สำเร็จ — API จะรันต่อแต่ endpoint ที่ต้องใช้ DB จะคืน error " +
            "(ตรวจ VPN และ ConnectionStrings:ServiceDb)");
    }
}

app.UseCors();
app.UseRateLimiter();
// Catalog edits can race with a receipt/issue; return a retryable conflict instead of an unhandled 500.
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (DbUpdateConcurrencyException) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new
        {
            success = false, data = (object?)null,
            error = new { code = "DATA_CONFLICT", messageTh = "ข้อมูลถูกแก้ไขพร้อมกัน กรุณาโหลดข้อมูลล่าสุดแล้วลองอีกครั้ง" },
            traceId = context.TraceIdentifier
        });
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", at = DateTime.UtcNow }));

app.Run();

public partial class Program;
