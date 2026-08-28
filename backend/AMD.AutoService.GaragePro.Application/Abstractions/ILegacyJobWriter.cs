using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// การเขียนจ๊อบลง Garage DB เดิมที่จำเป็นต่อหน้าเปิดจ๊อบเท่านั้น
/// ทุกคำสั่งต้องจำกัดสาขาจาก JWT และทำใน transaction เดียว
/// </summary>
public interface ILegacyJobWriter
{
    Task<JobFormOptionsDto> GetFormOptionsAsync(string shardKey, CancellationToken ct = default);

    Task<Result<CreatedLegacyJobDto>> CreateAsync(
        string shardKey,
        int branchId,
        long userId,
        CreateLegacyJobRequest request,
        CancellationToken ct = default);
}
