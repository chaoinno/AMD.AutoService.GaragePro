using AMD.AutoService.GaragePro.Application.Customers;
using AMD.AutoService.GaragePro.Application.Dtos;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class CustomerVehicleValidatorTests
{
    [Fact]
    public void Customer_requires_13_digit_id_card_when_supplied()
    {
        var request = new CustomerUpsertRequest("สมชาย", "ใจดี", "0812345678", IdCard: "1234");
        CustomerVehicleValidator.ValidateCustomer(request)?.Code.Should().Be("CUSTOMER_ID_CARD_INVALID");
    }

    [Fact]
    public void Blacklist_requires_a_reason()
    {
        var request = new CustomerUpsertRequest("สมชาย", "ใจดี", "0812345678", IsBlacklist: true);
        CustomerVehicleValidator.ValidateCustomer(request)?.Code.Should().Be("CUSTOMER_BLACKLIST_REMARK_REQUIRED");
    }

    [Fact]
    public void Vehicle_requires_customer_and_complete_model_selection()
    {
        var request = new VehicleUpsertRequest(0, "กข 1234", 1, 1, 1, 1, 1);
        CustomerVehicleValidator.ValidateVehicle(request)?.Code.Should().Be("VEHICLE_CUSTOMER_REQUIRED");
    }

    [Fact]
    public void Vin_must_be_17_alphanumeric_characters()
    {
        var request = new VehicleUpsertRequest(1, "กข 1234", 1, 1, 1, 1, 1, Vin: "SHORT");
        CustomerVehicleValidator.ValidateVehicle(request)?.Code.Should().Be("VEHICLE_VIN_INVALID");
    }
}
