using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AMD.AutoService.GaragePro.Application.Staff;

namespace AMD.AutoService.GaragePro.API.Controllers;

[ApiController]
[Authorize]
[RequireShiftSession]
public sealed class ReferenceDataController(ICustomerVehicleRepository repository, IStaffService staffService) : ControllerBase
{
    [HttpGet("api/v1/locations/provinces")]
    public async Task<IActionResult> Provinces(CancellationToken ct) => OkEnvelope(await repository.GetProvincesAsync(ct));

    [HttpGet("api/v1/locations/amphures")]
    public async Task<IActionResult> Amphures([FromQuery] int provinceId, CancellationToken ct) =>
        OkEnvelope(await repository.GetAmphuresAsync(provinceId, ct));

    [HttpGet("api/v1/locations/districts")]
    public async Task<IActionResult> Districts([FromQuery] int amphureId, CancellationToken ct) =>
        OkEnvelope(await repository.GetDistrictsAsync(amphureId, ct));

    [HttpGet("api/v1/locations/zipcode")]
    public async Task<IActionResult> ZipCode([FromQuery] int districtId, CancellationToken ct) =>
        OkEnvelope(new { zipCode = await repository.GetZipCodeAsync(districtId, ct) });

    [HttpGet("api/v1/cars/reference-data")]
    public async Task<IActionResult> VehicleReferenceData(CancellationToken ct) =>
        OkEnvelope(await repository.GetVehicleReferenceDataAsync(ct));

    [HttpGet("api/v1/cars/models")]
    public async Task<IActionResult> Models([FromQuery] int brandId, CancellationToken ct) =>
        OkEnvelope(await repository.GetModelsAsync(brandId, ct));

    [HttpGet("api/v1/cars/nicknames")]
    public async Task<IActionResult> Nicknames([FromQuery] int modelId, CancellationToken ct) =>
        OkEnvelope(await repository.GetNicknamesAsync(modelId, ct));

    [HttpGet("api/v1/departments")]
    public async Task<IActionResult> Departments(CancellationToken ct) =>
        Render(await staffService.GetReferenceDataAsync(ct), x => x.Departments);

    [HttpGet("api/v1/sectors")]
    public async Task<IActionResult> Sectors([FromQuery] int? departmentId, CancellationToken ct) =>
        OkResult(await staffService.GetSectorsAsync(departmentId, ct));

    [HttpGet("api/v1/positions")]
    public async Task<IActionResult> Positions(CancellationToken ct) =>
        Render(await staffService.GetReferenceDataAsync(ct), x => x.Positions);

    [HttpGet("api/v1/staff-skill-levels")]
    public async Task<IActionResult> SkillLevels(CancellationToken ct) =>
        Render(await staffService.GetReferenceDataAsync(ct), x => x.SkillLevels);

    private IActionResult OkEnvelope<T>(T data) =>
        Ok(Envelope.From(Result<T>.Ok(data), HttpContext.TraceIdentifier));

    private IActionResult OkResult<T>(Result<T> result) => result.Success
        ? Ok(Envelope.From(result, HttpContext.TraceIdentifier))
        : UnprocessableEntity(Envelope.From(result, HttpContext.TraceIdentifier));

    private IActionResult Render<T>(Result<StaffReferenceDataDto> result, Func<StaffReferenceDataDto,T> selector) =>
        result.Success ? OkEnvelope(selector(result.Data!)) : UnprocessableEntity(Envelope.From(result, HttpContext.TraceIdentifier));
}
