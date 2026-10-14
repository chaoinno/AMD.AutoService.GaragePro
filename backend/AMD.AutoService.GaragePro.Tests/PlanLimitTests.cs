using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Customers;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// ลิมิตจำนวนลูกค้าของแพ็กเกจทดลองใช้ (B5) — ตรวจหลังเช็คซ้ำ ก่อน insert · legacy ผ่านเสมอ
/// (สินค้า/จ๊อบ/พนักงานอยู่ใน CatalogServiceTests · JobServiceTests · StaffServiceTests)
/// </summary>
public sealed class PlanLimitTests
{
    private static readonly CustomerUpsertRequest NewCustomer = new("สมชาย", "ใจดี", "0812345678");

    [Fact]
    public async Task Customer_limit_blocks_new_customers()
    {
        var repository = new FakeCustomerRepository();
        var policy = new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial());
        policy.ReachedLimits.Add(PlanLimitKind.Customers);
        var service = new CustomerVehicleService(repository, new NoImages(), new User(), policy);

        var result = await service.CreateCustomerAsync(NewCustomer);

        result.Error!.Code.Should().Be(PlanErrors.LimitReachedCode);
        result.Error.MessageTh.Should().Contain("ลูกค้า").And.Contain("LINE @garagepro");
        repository.Created.Should().Be(0);
    }

    [Fact]
    public async Task Existing_customer_still_answers_duplicate_when_the_limit_is_reached()
    {
        var repository = new FakeCustomerRepository
        {
            Duplicate = new CustomerDuplicateDto(5, "C-5", "สมชาย ใจดี", "0812345678", null, false)
        };
        var policy = new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial());
        policy.ReachedLimits.Add(PlanLimitKind.Customers);
        var service = new CustomerVehicleService(repository, new NoImages(), new User(), policy);

        (await service.CreateCustomerAsync(NewCustomer)).Error!.Code.Should().Be("CUSTOMER_DUPLICATE");
        policy.LimitChecks.Should().BeEmpty();
    }

    [Fact]
    public async Task Under_the_limit_and_legacy_create_normally()
    {
        var underLimit = new FakeCustomerRepository();
        var policy = new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial());
        (await new CustomerVehicleService(underLimit, new NoImages(), new User(), policy).CreateCustomerAsync(NewCustomer))
            .Success.Should().BeTrue();
        underLimit.Created.Should().Be(1);
        policy.LimitChecks.Should().Equal(PlanLimitKind.Customers);

        var legacy = new FakeCustomerRepository();
        (await new CustomerVehicleService(legacy, new NoImages(), new User()).CreateCustomerAsync(NewCustomer))
            .Success.Should().BeTrue();
        legacy.Created.Should().Be(1);
    }

    private sealed class User : ICurrentUser
    {
        public long UserId => 42; public string UserName => "เจ้าของ"; public UserRole Role => UserRole.Manager;
        public string ShardKey => "sp1"; public int BranchId => 5001; public EventSource Source => EventSource.Web;
        public Guid? SessionId => null; public bool IsAdministrator => false;
    }

    private sealed class NoImages : IVehicleImageStorage
    {
        public VehicleImageValidation Validate(VehicleImageUpload upload) => new(true, null, null);
        public Task<string> SaveAsync(long vehicleId, VehicleImageUpload upload, CancellationToken ct = default) => throw new NotSupportedException();
        public bool TryResolve(string relativePath, out string fullPath) { fullPath = ""; return false; }
        public Task DeleteAsync(string relativePath, CancellationToken ct = default) => Task.CompletedTask;
        public string GetContentType(string relativePath) => "image/jpeg";
    }

    private sealed class FakeCustomerRepository : ICustomerVehicleRepository
    {
        public CustomerDuplicateDto? Duplicate { get; init; }
        public int Created { get; private set; }

        public Task<CustomerDuplicateDto?> FindDuplicateCustomerAsync(LegacyRequestScope scope, CustomerUpsertRequest request,
            long? excludingId, CancellationToken ct = default) => Task.FromResult(Duplicate);

        public Task<long> CreateCustomerAsync(LegacyRequestScope scope, CustomerUpsertRequest request, CancellationToken ct = default)
        {
            Created++;
            return Task.FromResult(100L + Created);
        }

        public Task<CustomerDetailDto?> GetCustomerAsync(LegacyRequestScope scope, long id, CancellationToken ct = default) =>
            Task.FromResult<CustomerDetailDto?>(null);

        public Task<PagedResult<CustomerSummaryDto>> SearchCustomersAsync(LegacyRequestScope scope, CustomerSearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CustomerSummaryDto>> ExportCustomersAsync(LegacyRequestScope scope, CustomerSearchQuery query, int take, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> UpdateCustomerAsync(LegacyRequestScope scope, long id, CustomerUpsertRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SoftDeleteCustomerAsync(LegacyRequestScope scope, long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<VehicleSummaryDto>> SearchVehiclesAsync(LegacyRequestScope scope, VehicleSearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<VehicleSummaryDto>> ExportVehiclesAsync(LegacyRequestScope scope, VehicleSearchQuery query, int take, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<VehicleDetailDto?> GetVehicleAsync(LegacyRequestScope scope, long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> CreateVehicleAsync(LegacyRequestScope scope, VehicleUpsertRequest request, VehicleImageUpload? image, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> UpdateVehicleAsync(LegacyRequestScope scope, long id, VehicleUpsertRequest request, VehicleImageUpload? image, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SoftDeleteVehicleAsync(LegacyRequestScope scope, long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> GetVehicleImagePathAsync(LegacyRequestScope scope, long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> UpdateVehicleImageAsync(LegacyRequestScope scope, long id, VehicleImageUpload image, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LookupItemDto>> GetProvincesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LookupItemDto>> GetAmphuresAsync(int provinceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LookupItemDto>> GetDistrictsAsync(int amphureId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> GetZipCodeAsync(int districtId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<VehicleReferenceDataDto> GetVehicleReferenceDataAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LookupItemDto>> GetModelsAsync(int brandId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LookupItemDto>> GetNicknamesAsync(int modelId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
