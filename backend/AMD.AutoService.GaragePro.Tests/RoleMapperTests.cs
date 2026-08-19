using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// การ map โครงองค์กรเดิม → บทบาทในระบบใหม่
/// [ASSUME] ทั้งชุดนี้เป็นการเดา — test นี้ล็อกพฤติกรรมไว้ให้เปลี่ยนแล้วรู้ตัว
/// </summary>
public class RoleMapperTests
{
    [Fact]
    public void Administrator_flag_wins_over_everything()
    {
        RoleMapper.Resolve(true, "พนักงาน", "เคาะ", "ฝ่ายโรงงาน")
            .Should().Be(UserRole.Manager);
    }

    [Theory]
    [InlineData("ผู้บริหาร")]
    [InlineData("ผู้จัดการ")]
    public void Executive_positions_are_managers_regardless_of_department(string position)
    {
        RoleMapper.Resolve(false, position, "เคาะ", "ฝ่ายโรงงาน")
            .Should().Be(UserRole.Manager);
    }

    [Fact]
    public void Workshop_department_head_is_lead()
    {
        RoleMapper.Resolve(false, "หัวหน้าแผนก", "เคาะ", "ฝ่ายโรงงาน")
            .Should().Be(UserRole.Lead);
    }

    [Theory]
    [InlineData("เคาะ")]
    [InlineData("พ่นสี")]
    [InlineData("เตรียมพื้น")]
    [InlineData("รื้อ - ประกอบ")]
    [InlineData("ขัดยา")]
    public void Workshop_staff_are_technicians(string sector)
    {
        RoleMapper.Resolve(false, "พนักงาน", sector, "ฝ่ายโรงงาน")
            .Should().Be(UserRole.Technician);
    }

    [Fact]
    public void Qc_sector_is_technician_even_outside_workshop_department()
    {
        RoleMapper.Resolve(false, "พนักงาน", "QC", "ฝ่ายฟรอนท์")
            .Should().Be(UserRole.Technician);
    }

    [Fact]
    public void Vehicle_intake_is_front_desk()
    {
        RoleMapper.Resolve(false, "พนักงาน", "รับรถ", "ฝ่ายฟรอนท์")
            .Should().Be(UserRole.FrontDesk);
    }

    [Theory]
    [InlineData("ประเมินราคา")]
    [InlineData("รับรถ/ประเมินราคา")]   // มีทั้งสองคำ — "รับรถ" ต้องชนะเพราะเป็นงานหน้าร้าน
    public void Estimating_sectors_route_by_first_match(string sector)
    {
        var role = RoleMapper.Resolve(false, "พนักงาน", sector, "ฝ่ายฟรอนท์");
        role.Should().Be(sector.Contains("รับรถ") ? UserRole.FrontDesk : UserRole.Office);
    }

    [Fact]
    public void Accounting_is_cashier()
    {
        RoleMapper.Resolve(false, "พนักงาน", "บัญชี", "ฝ่ายแบคออฟฟิต")
            .Should().Be(UserRole.Cashier);
    }

    [Fact]
    public void Other_back_office_is_office()
    {
        RoleMapper.Resolve(false, "พนักงาน", "สารบรรณ", "ฝ่ายแบคออฟฟิต")
            .Should().Be(UserRole.Office);
    }

    [Fact]
    public void Parts_department_is_office()
    {
        RoleMapper.Resolve(false, "พนักงาน", "อะไหล่", "ฝ่ายอะไหล่")
            .Should().Be(UserRole.Office);
    }

    [Fact]
    public void Unknown_org_data_falls_back_to_least_privilege()
    {
        RoleMapper.Resolve(false, null, null, null)
            .Should().Be(UserRole.FrontDesk, "เดาสูงเกินอันตรายกว่าเดาต่ำเกิน");
    }

    [Theory]
    [InlineData(UserRole.Technician, false)]
    [InlineData(UserRole.Lead, false)]
    [InlineData(UserRole.FrontDesk, true)]
    [InlineData(UserRole.Cashier, true)]
    [InlineData(UserRole.Office, true)]
    [InlineData(UserRole.Manager, true)]
    public void Technicians_cannot_close_shift(UserRole role, bool canClose)
    {
        RoleMapper.CanCloseShift(role).Should().Be(canClose);
    }
}
