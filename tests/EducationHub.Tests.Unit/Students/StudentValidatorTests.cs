using EducationHub.Application;
using EducationHub.Application.Features.Students.Commands;
using EducationHub.Application.Features.Students.DTOs;
using EducationHub.Application.Features.Students.Validators;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Constants;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace EducationHub.Tests.Unit.Students;

/// <summary>Mission Phase 2.5: validators and permission catalog for students/parents.</summary>
public sealed class StudentValidatorTests
{
    private static CreateStudentRequest Student(
        string? fullName = "Omar Hassan",
        Guid? userId = null,
        string? accountEmail = null,
        string? accountPassword = null)
        => new(fullName, null, null, null, null, null, null, userId, accountEmail, accountPassword);

    private static CreateParentRequest Parent(
        string? fullName = "Mona Ali",
        string? email = null,
        Guid? userId = null,
        string? accountPassword = null)
        => new(fullName, null, email, null, userId, accountPassword);

    [Fact]
    public void Student_without_account_is_valid()
        => Assert.True(new CreateStudentRequestValidator().Validate(Student()).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Al")]
    public void Student_needs_a_name_of_three_characters(string? name)
        => Assert.False(new CreateStudentRequestValidator().Validate(Student(fullName: name)).IsValid);

    [Fact]
    public void Linking_and_creating_an_account_together_is_rejected()
    {
        var result = new CreateStudentRequestValidator().Validate(
            Student(userId: Guid.NewGuid(), accountEmail: "kid@example.test", accountPassword: "Passw0rd"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void New_student_account_needs_a_strong_enough_password()
    {
        var validator = new CreateStudentRequestValidator();

        Assert.False(validator.Validate(Student(accountEmail: "kid@example.test", accountPassword: "short")).IsValid);
        Assert.False(validator.Validate(Student(accountEmail: "kid@example.test", accountPassword: "noDigitsHere")).IsValid);
        Assert.True(validator.Validate(Student(accountEmail: "kid@example.test", accountPassword: "Passw0rd")).IsValid);
    }

    [Fact]
    public void A_password_without_an_account_email_is_rejected()
        => Assert.False(new CreateStudentRequestValidator().Validate(Student(accountPassword: "Passw0rd")).IsValid);

    [Fact]
    public void Parent_account_needs_the_contact_email()
    {
        var validator = new CreateParentRequestValidator();

        Assert.False(validator.Validate(Parent(accountPassword: "Passw0rd")).IsValid);
        Assert.True(validator.Validate(Parent(email: "mona@example.test", accountPassword: "Passw0rd")).IsValid);
        Assert.True(validator.Validate(Parent()).IsValid);
    }

    [Fact]
    public void Guardian_list_rejects_duplicates_and_two_primaries()
    {
        var validator = new SetStudentGuardiansRequestValidator();
        var parent = Guid.NewGuid();

        var duplicate = new SetStudentGuardiansRequest(
        [
            new GuardianLinkRequest(parent, GuardianRelationship.Mother, true),
            new GuardianLinkRequest(parent, GuardianRelationship.Father, false)
        ]);

        var twoPrimary = new SetStudentGuardiansRequest(
        [
            new GuardianLinkRequest(Guid.NewGuid(), GuardianRelationship.Mother, true),
            new GuardianLinkRequest(Guid.NewGuid(), GuardianRelationship.Father, true)
        ]);

        var valid = new SetStudentGuardiansRequest(
        [
            new GuardianLinkRequest(Guid.NewGuid(), GuardianRelationship.Mother, true),
            new GuardianLinkRequest(Guid.NewGuid(), GuardianRelationship.Father, false)
        ]);

        Assert.False(validator.Validate(duplicate).IsValid);
        Assert.False(validator.Validate(twoPrimary).IsValid);
        Assert.True(validator.Validate(valid).IsValid);
        Assert.True(validator.Validate(new SetStudentGuardiansRequest([])).IsValid);
        Assert.False(validator.Validate(new SetStudentGuardiansRequest(null)).IsValid);
    }

    [Fact]
    public void Student_and_parent_commands_have_registered_validators()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();
        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IValidator<CreateStudentCommand>>());
        Assert.NotNull(provider.GetService<IValidator<UpdateStudentCommand>>());
        Assert.NotNull(provider.GetService<IValidator<SetStudentGuardiansCommand>>());
        Assert.NotNull(provider.GetService<IValidator<CreateParentCommand>>());
        Assert.NotNull(provider.GetService<IValidator<UpdateParentCommand>>());
    }

    [Fact]
    public void Permission_catalog_carries_the_students_and_parents_areas()
    {
        var all = PermissionConstants.GetAll();

        Assert.Contains(PermissionConstants.StudentsView, all);
        Assert.Contains(PermissionConstants.ParentsViewOwnChildren, all);
        Assert.True(PermissionConstants.Areas.ContainsKey("Students"));
        Assert.True(PermissionConstants.Areas.ContainsKey("Parents"));
    }
}
