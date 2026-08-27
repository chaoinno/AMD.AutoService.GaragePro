using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

[ApiController]
[Authorize]
[RequireShiftSession]
public sealed class ReferenceDataController(ICustomerVehicleRepository repository) : ControllerBase
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

    private IActionResult OkEnvelope<T>(T data) =>
        Ok(Envelope.From(Result<T>.Ok(data), HttpContext.TraceIdentifier));
}
