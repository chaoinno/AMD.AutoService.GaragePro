using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// ส่วนของ StarterDataRepository ที่ตรวจได้โดยไม่ต่อฐาน (resource ลายเซ็นที่ฝังมากับ build) + SQL จริงกับฐาน sp1 ที่ provision แล้ว
/// (env-gated เหมือน SelfServeShardSqlTests — อ่านอย่างเดียว · คำสั่งลบรันด้วย id ที่ไม่มีอยู่ภายใน transaction ที่ rollback เสมอ)
/// </summary>
public class StarterDataRepositoryTests
{
    private static string Connection => Environment.GetEnvironmentVariable(SelfServeShardSqlFactAttribute.Variable)!;

    [Fact]
    public void Embedded_sample_signature_is_a_png_of_a_signature_sized_canvas()
    {
        // GetSampleSignaturePng ไม่แตะ dependency ใดเลย — ส่ง null ได้
        var repository = new StarterDataRepository(null!, null!, null!, null!, null!);

        var png = repository.GetSampleSignaturePng();

        png.Should().HaveCountGreaterThan(100);
        png.Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        // IHDR: width/height big-endian ที่ไบต์ 16–23
        var width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        var height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        (width, height).Should().Be((200, 80));
        png.Length.Should().BeLessThan(5 * 1024 * 1024, "ต้องผ่าน AttachmentStorage.Validate (ขนาดไฟล์)");
    }

    [SelfServeShardSqlFact]
    public async Task Vehicle_reference_queries_find_a_brand_model_nickname_year_and_province_through_the_synonyms()
    {
        await using var db = new SqlConnection(Connection);

        var models = (await db.QueryAsync<SampleVehicleModel>(StarterDataRepository.VehicleModelsSql, new { Take = 2 })).ToList();
        models.Should().NotBeEmpty();
        models.Should().OnlyContain(m => m.BrandId > 0 && m.ModelId > 0 && m.NicknameId > 0);
        models.Select(m => m.BrandName.ToUpperInvariant()).Should().OnlyContain(n => n == "TOYOTA" || n == "HONDA" || n == "ISUZU");

        (await db.QueryFirstOrDefaultAsync<int?>(StarterDataRepository.YearSql, new { Ad = "2023" })).Should().NotBeNull();
        (await db.QueryFirstOrDefaultAsync<int?>(StarterDataRepository.ProvinceSql, new { ProvinceId = 0 })).Should().NotBeNull();
    }

    [SelfServeShardSqlFact]
    public async Task Delete_statements_bind_every_shadow_column_and_report_not_found_for_unknown_rows()
    {
        await using var db = new SqlConnection(Connection);
        await db.OpenAsync();
        await using var guard = (SqlTransaction)await db.BeginTransactionAsync();
        try
        {
            (await db.ExecuteScalarAsync<int>(StarterDataRepository.DeleteVehicleSql, new { Id = -1L, BranchId = 0 }, guard))
                .Should().Be(0);
            (await db.ExecuteScalarAsync<int>(StarterDataRepository.DeleteCustomerSql, new { Id = -1L, BranchId = 0 }, guard))
                .Should().Be(0);
            (await db.QueryFirstOrDefaultAsync(StarterDataStore.OwnerSql, new { UserId = -1L }, guard)).Should().BeNull();
        }
        finally
        {
            await guard.RollbackAsync();
        }
    }
}
