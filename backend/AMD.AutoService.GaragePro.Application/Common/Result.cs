namespace AMD.AutoService.GaragePro.Application.Common;

/// <summary>
/// Envelope เดียวกันทั้ง API — [UI] ทุก error state ต้องมี สาเหตุ + ปุ่มถัดไป + รหัสอ้างอิง
/// </summary>
public sealed record ApiError(string Code, string MessageTh, string? Field = null, object? Details = null);

public sealed class Result<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public ApiError? Error { get; init; }

    public static Result<T> Ok(T data) => new() { Success = true, Data = data };

    public static Result<T> Fail(string code, string messageTh, string? field = null, object? details = null) =>
        new() { Success = false, Error = new ApiError(code, messageTh, field, details) };

    public static Result<T> Fail(ApiError error) => new() { Success = false, Error = error };
}
