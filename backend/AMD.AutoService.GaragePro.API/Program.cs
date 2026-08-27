using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AMD.AutoService.GaragePro.API;
using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Infrastructure;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
        Description = "ใส่ accessToken ที่ได้จาก /auth/shift-sessions"
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

var app = builder.Build();

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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", at = DateTime.UtcNow }));

app.Run();

public partial class Program;
