using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Staff;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class StaffServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cannot_create_update_or_preview_in_another_branch_even_as_admin(bool admin)
    {
        var repository = new StubRepository(Detail());
        var service = new StaffService(repository, new StubImageStorage(), new StubUser(admin));
        var foreign = Input() with { BranchId = 2 };

        Assert.Equal("STAFF_BRANCH_FORBIDDEN", (await service.CreateAsync(foreign, null)).Error?.Code);
        Assert.Equal("STAFF_BRANCH_FORBIDDEN", (await service.UpdateAsync(10, foreign, null)).Error?.Code);
        Assert.Equal("STAFF_BRANCH_FORBIDDEN", (await service.PreviewCodeAsync(2)).Error?.Code);
        Assert.Null(repository.CreatedRequest);
        Assert.False(repository.UpdateCalled);
        Assert.Null(repository.PreviewBranch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Omitted_branch_uses_login_branch_for_create_update_and_preview(bool admin)
    {
        var repository = new StubRepository(Detail());
        var service = new StaffService(repository, new StubImageStorage(), new StubUser(admin));
        var input = Input() with { BranchId = 0 };

        Assert.True((await service.CreateAsync(input, null)).Success);
        Assert.Equal(1, repository.CreatedRequest?.BranchId);
        Assert.True((await service.UpdateAsync(10, input, null)).Success);
        Assert.Equal(1, repository.UpdatedRequest?.BranchId);
        Assert.True((await service.PreviewCodeAsync(null)).Success);
        Assert.Equal(1, repository.PreviewBranch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Foreign_staff_detail_cannot_be_returned_or_updated(bool admin)
    {
        var repository = new StubRepository(Detail() with { BranchId = 2 });
        var service = new StaffService(repository, new StubImageStorage(), new StubUser(admin));

        Assert.Equal("STAFF_NOT_FOUND", (await service.GetAsync(10)).Error?.Code);
        Assert.Equal("STAFF_NOT_FOUND", (await service.UpdateAsync(10, Input(), null)).Error?.Code);
        Assert.False(repository.UpdateCalled);
    }

    [Fact]
    public async Task Non_admin_cannot_change_organization()
    {
        var repository = new StubRepository(Detail());
        var service = new StaffService(repository, new StubImageStorage(), new StubUser(false));

        var result = await service.UpdateAsync(10, Input() with { MainSectorId = 99 }, null);

        Assert.False(result.Success);
        Assert.Equal("STAFF_ORGANIZATION_FORBIDDEN", result.Error?.Code);
        Assert.False(repository.UpdateCalled);
    }

    [Fact]
    public async Task Admin_can_change_organization()
    {
        var repository = new StubRepository(Detail());
        var service = new StaffService(repository, new StubImageStorage(), new StubUser(true));

        var result = await service.UpdateAsync(10, Input() with { MainSectorId = 99 }, null);

        Assert.True(result.Success);
        Assert.True(repository.UpdateCalled);
    }

    private static StaffUpsertRequest Input() => new(1,"สมชาย","ใจดี",1,null,null,null,null,null,null,null,
        "0812345678",null,null,null,10000,null,1,0,DateTime.Today,null,null,2,3,[],"staff",null);

    private static StaffDetailDto Detail() => new(
        Id: 10, BranchId: 1, BranchName: "สาขา", Code: "2608010001", FirstName: "สมชาย", LastName: "ใจดี",
        GenderId: 1, GenderName: "ชาย", IdCard: null, Address1: null, Address2: null, ProvinceId: null,
        ProvinceName: null, AmphureId: null, AmphureName: null, DistrictId: null, DistrictName: null, ZipCode: null,
        PhoneNumber1: "0812345678", PhoneNumber2: null, Email: null, LineId: null, Salary: 10000,
        StaffSkillLevelId: null, StaffSkillLevelName: null, ExperienceYear: 1, ExperienceMonth: 0,
        StartJobDate: DateTime.Today, EndJobDate: null, Note: null, PictureUrl: null, IsActive: true,
        Account: new(20,"staff",false,true), SectorPositions: [new(30,1,"ฝ่าย",2,"แผนก",3,"ตำแหน่ง",true)],
        CreatedDate: DateTime.Today, LastUpdated: DateTime.Today);

    private sealed class StubUser(bool admin) : ICurrentUser
    {
        public long UserId => 1; public string UserName => "ผู้ทดสอบ"; public UserRole Role => UserRole.FrontDesk;
        public string ShardKey => "db2"; public int BranchId => 1; public EventSource Source => EventSource.Web;
        public Guid? SessionId => null; public bool IsAdministrator => admin;
    }

    private sealed class StubImageStorage : IStaffImageStorage
    {
        public StaffImageValidation Validate(StaffImageUpload upload) => new(true,null,null);
        public Task<string> SaveAsync(long staffId, StaffImageUpload upload, CancellationToken ct = default) => Task.FromResult("");
        public bool TryResolve(string relativePath, out string fullPath) { fullPath = ""; return false; }
        public Task DeleteAsync(string relativePath, CancellationToken ct = default) => Task.CompletedTask;
        public string GetContentType(string relativePath) => "image/jpeg";
    }

    private sealed class StubRepository(StaffDetailDto detail) : IStaffRepository
    {
        public bool UpdateCalled { get; private set; }
        public StaffUpsertRequest? CreatedRequest { get; private set; }
        public StaffUpsertRequest? UpdatedRequest { get; private set; }
        public int? PreviewBranch { get; private set; }
        public Task<StaffDetailDto?> GetAsync(LegacyRequestScope scope, bool isAdministrator, long id, CancellationToken ct = default) => Task.FromResult<StaffDetailDto?>(detail);
        public Task<StaffDetailDto?> UpdateAsync(LegacyRequestScope scope, bool isAdministrator, long id, StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default) { UpdateCalled = true; UpdatedRequest = request; return Task.FromResult<StaffDetailDto?>(detail); }
        public Task<PagedResult<StaffSummaryDto>> SearchAsync(LegacyRequestScope scope, bool isAdministrator, StaffSearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StaffDetailDto?> CreateAsync(LegacyRequestScope scope, bool isAdministrator, StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default) { CreatedRequest = request; return Task.FromResult<StaffDetailDto?>(detail); }
        public Task<bool> SetStatusAsync(LegacyRequestScope scope, bool isAdministrator, long id, StaffStatusRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StaffCodePreviewDto> PreviewCodeAsync(LegacyRequestScope scope, int branchId, CancellationToken ct = default) { PreviewBranch = branchId; return Task.FromResult(new StaffCodePreviewDto("test", "test", "test")); }
        public Task<string?> GetImagePathAsync(LegacyRequestScope scope, bool isAdministrator, long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StaffReferenceDataDto> GetReferenceDataAsync(LegacyRequestScope scope, bool isAdministrator, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LookupItemDto>> GetSectorsAsync(LegacyRequestScope scope, int? departmentId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
