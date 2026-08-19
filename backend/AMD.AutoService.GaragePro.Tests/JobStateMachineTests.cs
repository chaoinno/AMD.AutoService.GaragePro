using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Domain.StateMachine;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// กฎ lifecycle จาก transition table ใน docs/01-workflow.md §1
/// ทุก guard ต้องบล็อกจริง ไม่ใช่แค่ซ่อนปุ่มที่ client
/// </summary>
public class JobStateMachineTests
{
    private const JobGuard All = (JobGuard)~0;

    [Fact]
    public void Table_has_all_twelve_documented_transitions()
    {
        // 11 เส้นทางปกติ + ยกเลิก (ใช้ได้จากทุกสถานะ) = 12 ตามเอกสาร
        JobStateMachine.Transitions.Should().HaveCount(11);
        JobStateMachine.CancelTransition.To.Should().Be(JobStatus.Cancelled);
    }

    [Fact]
    public void Technician_can_submit_inspection_when_complete()
    {
        var result = JobStateMachine.CanTransition(
            JobStatus.WaitInspect, JobStatus.WaitQuote,
            UserRole.Technician, EventSource.Mobile,
            JobGuard.InspectionComplete);

        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public void Incomplete_inspection_is_blocked_with_thai_reason()
    {
        var result = JobStateMachine.CanTransition(
            JobStatus.WaitInspect, JobStatus.WaitQuote,
            UserRole.Technician, EventSource.Mobile,
            JobGuard.None);

        result.Allowed.Should().BeFalse();
        result.ErrorCode.Should().Be("JOB_GUARD_NOT_SATISFIED");
        result.MessageTh.Should().Contain("ผลตรวจยังไม่ครบ");
    }

    [Fact]
    public void Cashier_cannot_submit_inspection()
    {
        var result = JobStateMachine.CanTransition(
            JobStatus.WaitInspect, JobStatus.WaitQuote,
            UserRole.Cashier, EventSource.Mobile, All);

        result.Allowed.Should().BeFalse();
        result.ErrorCode.Should().Be("JOB_TRANSITION_FORBIDDEN_ROLE");
    }

    [Fact]
    public void Quotation_must_be_sent_from_web_not_mobile()
    {
        var fromWeb = JobStateMachine.CanTransition(
            JobStatus.WaitQuote, JobStatus.WaitApprove,
            UserRole.Office, EventSource.Web, All);

        var fromMobile = JobStateMachine.CanTransition(
            JobStatus.WaitQuote, JobStatus.WaitApprove,
            UserRole.Office, EventSource.Mobile, All);

        fromWeb.Allowed.Should().BeTrue();
        fromMobile.Allowed.Should().BeFalse();
        fromMobile.ErrorCode.Should().Be("JOB_TRANSITION_FORBIDDEN_SOURCE");
    }

    [Fact]
    public void Customer_approval_requires_both_all_lines_decided_and_at_least_one_approved()
    {
        var signedButNothingApproved = JobStateMachine.CanTransition(
            JobStatus.WaitApprove, JobStatus.Approved,
            UserRole.FrontDesk, EventSource.Mobile,
            JobGuard.AllLinesDecidedAndSigned);

        signedButNothingApproved.Allowed.Should().BeFalse();
        signedButNothingApproved.MissingGuards.Should().HaveFlag(JobGuard.HasApprovedLines);
    }

    [Fact]
    public void Revision_keeps_job_in_waitapprove()
    {
        var result = JobStateMachine.CanTransition(
            JobStatus.WaitApprove, JobStatus.WaitApprove,
            UserRole.Office, EventSource.Web, JobGuard.QuotationValid);

        result.Allowed.Should().BeTrue("ออกเวอร์ชันใหม่ไม่เปลี่ยนสถานะงาน แต่ล้างการอนุมัติเดิม");
    }

    [Fact]
    public void Cannot_send_to_qc_without_before_after_photos()
    {
        var result = JobStateMachine.CanTransition(
            JobStatus.InProgress, JobStatus.Qc,
            UserRole.Technician, EventSource.Mobile, JobGuard.None);

        result.Allowed.Should().BeFalse();
        result.MessageTh.Should().Contain("รูปก่อน-หลังไม่ครบ");
    }

    [Fact]
    public void Completing_requires_balance_document_and_handover_together()
    {
        var result = JobStateMachine.CanTransition(
            JobStatus.Ready, JobStatus.Completed,
            UserRole.Cashier, EventSource.Web,
            JobGuard.BalanceSettled | JobGuard.DocumentIssued);

        result.Allowed.Should().BeFalse();
        result.MissingGuards.Should().HaveFlag(JobGuard.VehicleHandedOver);
    }

    [Fact]
    public void Cancel_requires_reason_and_approver()
    {
        var withoutReason = JobStateMachine.CanTransition(
            JobStatus.InProgress, JobStatus.Cancelled,
            UserRole.Manager, EventSource.Web, JobGuard.None);

        var withReason = JobStateMachine.CanTransition(
            JobStatus.InProgress, JobStatus.Cancelled,
            UserRole.Manager, EventSource.Web, JobGuard.CancelReasonAndApprover);

        withoutReason.Allowed.Should().BeFalse();
        withReason.Allowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Cancelled)]
    public void Terminal_jobs_cannot_be_cancelled_again(JobStatus terminal)
    {
        var result = JobStateMachine.CanTransition(
            terminal, JobStatus.Cancelled, UserRole.Manager, EventSource.Web, All);

        result.Allowed.Should().BeFalse();
        result.ErrorCode.Should().Be("JOB_ALREADY_CLOSED");
    }

    [Fact]
    public void Skipping_states_is_rejected()
    {
        var result = JobStateMachine.CanTransition(
            JobStatus.WaitInspect, JobStatus.Ready,
            UserRole.Manager, EventSource.Web, All);

        result.Allowed.Should().BeFalse();
        result.ErrorCode.Should().Be("JOB_TRANSITION_NOT_ALLOWED");
    }

    [Fact]
    public void Next_actions_are_offered_for_open_jobs_only()
    {
        JobStateMachine.NextFrom(JobStatus.Qc).Select(t => t.To)
            .Should().BeEquivalentTo([JobStatus.InProgress, JobStatus.Ready]);

        JobStateMachine.NextFrom(JobStatus.Completed).Should().BeEmpty();
    }

    [Fact]
    public void Status_tokens_are_stable_across_platforms()
    {
        JobStateMachine.ToToken(JobStatus.WaitInspect).Should().Be("waitinspect");
        JobStateMachine.ToToken(JobStatus.WaitParts).Should().Be("waitparts");
        JobStateMachine.ToToken(JobStatus.Completed).Should().Be("completed");
    }
}
