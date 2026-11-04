using EducationHub.Application.Features.Tenancy.DTOs;
using EducationHub.Application.Features.Tenancy.Validators;
using EducationHub.Domain.Enums;

namespace EducationHub.Tests.Unit.Tenancy;

/// <summary>
/// The administrator fields on tenant creation, which are the gate to a tenant existing with
/// a usable account. A lenient validator here would pass the bad value to Identity, where it
/// would fail as a generic "identity error" instead of a clean 400 with a message.
/// </summary>
public sealed class CreateTenantRequestValidatorTests
{
    private readonly CreateTenantRequestValidator _validator = new();

    [Fact]
    public void Valid_request_passes()
    {
        var result = _validator.Validate(Valid());

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("admin@")] 
    public void AdminEmail_must_be_a_valid_address(string email)
    {
        var result = _validator.Validate(Valid() with { AdminEmail = email });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTenantRequest.AdminEmail));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Short1")]     // short
    [InlineData("no-digits")]
    [InlineData("12345678")]   // no letters
    public void AdminPassword_must_satisfy_identity_policy(string password)
    {
        var result = _validator.Validate(Valid() with { AdminPassword = password });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTenantRequest.AdminPassword));
    }

    [Fact]
    public void AdminFullName_is_required()
    {
        var result = _validator.Validate(Valid() with { AdminFullName = string.Empty });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTenantRequest.AdminFullName));
    }

    [Fact]
    public void AdminFullName_rejects_slash_dot_invalid_chars()
    {
        var result = _validator.Validate(Valid() with { AdminFullName = "Admin<script>" });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTenantRequest.AdminFullName));
    }

    private static CreateTenantRequest Valid()
        => new(
            Slug: "academy-one",
            Name: "Academy One",
            NameAr: null,
            Type: TenantType.EducationCenter,
            Email: "contact@academy.test",
            Phone: null,
            Address: null,
            AddressAr: null,
            City: "Cairo",
            Country: "EG",
            Currency: "EGP",
            TimeZone: "Africa/Cairo",
            DefaultLanguage: "ar",
            Description: null,
            DescriptionAr: null,
            AdminFullName: "Sara Khalil",
            AdminEmail: "sara@academy.test",
            AdminPassword: "Adm1nPassw0rd");
}