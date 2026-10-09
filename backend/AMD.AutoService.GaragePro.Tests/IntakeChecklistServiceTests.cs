using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Intake;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class IntakeChecklistServiceTests
{
    private static readonly Guid TestJobId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public async Task SubmitAsync_requires_intake_mileage()
    {
        var (service, repo) = Create(mileage: null);

        var result = await service.SubmitAsync(TestJobId);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("INTAKE_MILEAGE_REQUIRED");
        repo.Checklist!.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitAsync_locks_the_checklist_when_mileage_is_recorded()
    {
        var (service, repo) = Create(mileage: 45_000);

        var result = await service.SubmitAsync(TestJobId);

        result.Success.Should().BeTrue();
        repo.Checklist!.IsLocked.Should().BeTrue();
    }

    private static (IntakeChecklistService, FakeIntakeRepository) Create(int? mileage)
    {
        var job = new Job
        {
            Id = TestJobId, LegacyShardKey = "db2", BranchId = 105, JobNo = "JB1",
            JobTypeId = 9, Status = JobStatus.WaitInspect, MileageAtIntake = mileage
        };
        var checklist = new IntakeChecklist { JobId = TestJobId };
        checklist.Items = IntakeChecklistTemplate.Items.Select(t => new IntakeChecklistItem
        {
            IntakeChecklistId = checklist.Id, CategoryKey = t.CategoryKey, ItemCode = t.ItemCode,
            Result = IntakeCheckResult.Ok
        }).ToList();
        var repo = new FakeIntakeRepository { Checklist = checklist };
        return (new IntakeChecklistService(repo, new FakeJobRepository(job), new StubUser(), TimeProvider.System), repo);
    }

    private sealed class FakeIntakeRepository : IIntakeChecklistRepository
    {
        public IntakeChecklist? Checklist { get; set; }
        public Task<IntakeChecklist?> GetByJobAsync(Guid jobId, CancellationToken ct = default) => Task.FromResult(Checklist);
        public Task AddAsync(IntakeChecklist checklist, CancellationToken ct = default)
        {
            Checklist = checklist;
            return Task.CompletedTask;
        }
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeJobRepository(Job job) : IJobRepository
    {
        public Task<Job?> GetAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult<Job?>(jobId == job.Id ? job : null);
        public Task<Job?> GetOpenByVehicleAsync(string shardKey, int branchId, long vehicleId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<Job>> SearchAsync(JobSearchQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<Job>> GetAppointmentsAsync(JobAppointmentQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<int> CountOpenAsync(string shardKey, int branchId, int? jobTypeId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<JobStatusTally>> CountOpenByStatusAsync(
            string shardKey, int branchId, int? jobTypeId, DateTime nowUtc, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task AddAsync(Job j, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class StubUser : ICurrentUser
    {
        public long UserId => 7;
        public string UserName => "พนักงาน ทดสอบ";
        public UserRole Role => UserRole.FrontDesk;
        public string ShardKey => "db2";
        public int BranchId => 105;
        public EventSource Source => EventSource.Web;
        public bool IsAdministrator => false;
        public Guid? SessionId => null;
    }
}
