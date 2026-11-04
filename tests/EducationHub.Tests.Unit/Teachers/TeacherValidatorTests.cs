using EducationHub.Application.Features.Teachers.Commands;
using EducationHub.Application.Features.Teachers.DTOs;
using EducationHub.Application.Features.Teachers.Validators;
using EducationHub.Domain.Enums;

namespace EducationHub.Tests.Unit.Teachers;

/// <summary>
/// Request-level rules for the teachers slice: the create-or-link split, subject list
/// sanity and compensation rate bounds.
/// </summary>
public sealed class TeacherValidatorTests
{
    [Fact]
    public void Valid_new_account_request_passes()
    {
        var result = new CreateTeacherRequestValidator().Validate(NewAccount());

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void Valid_link_request_passes()
    {
        var result = new CreateTeacherRequestValidator().Validate(Link());

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void New_account_mode_requires_a_valid_email(string email)
    {
        var result = new CreateTeacherRequestValidator().Validate(NewAccount() with { Email = email });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTeacherRequest.Email));
    }

    [Fact]
    public void New_account_mode_requires_a_password_meeting_the_identity_policy()
    {
        var weak = NewAccount() with { Password = "short" };

        var result = new CreateTeacherRequestValidator().Validate(weak);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTeacherRequest.Password));
    }

    [Fact]
    public void New_account_mode_requires_a_full_name()
    {
        var result = new CreateTeacherRequestValidator().Validate(NewAccount() with { FullName = "" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTeacherRequest.FullName));
    }

    [Fact]
    public void Link_mode_rejects_credentials_that_belong_to_a_new_account()
    {
        var request = Link() with { Email = "extra@example.test", Password = "Passw0rd9" };

        var result = new CreateTeacherRequestValidator().Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTeacherRequest.Email));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTeacherRequest.Password));
    }

    [Fact]
    public void Duplicate_subjects_are_rejected()
    {
        var subjectId = Guid.NewGuid();

        var request = NewAccount() with { SubjectIds = [subjectId, subjectId] };

        var result = new CreateTeacherRequestValidator().Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTeacherRequest.SubjectIds));
    }

    [Fact]
    public void Assign_rejects_a_null_subject_list()
    {
        var result = new AssignTeacherSubjectsRequestValidator()
            .Validate(new AssignTeacherSubjectsRequest(null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AssignTeacherSubjectsRequest.SubjectIds));
    }

    [Fact]
    public void Assign_accepts_an_empty_list_to_clear_the_set()
    {
        var result = new AssignTeacherSubjectsRequestValidator()
            .Validate(new AssignTeacherSubjectsRequest([]));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void Assign_rejects_duplicate_subjects()
    {
        var subjectId = Guid.NewGuid();

        var result = new AssignTeacherSubjectsRequestValidator()
            .Validate(new AssignTeacherSubjectsRequest([subjectId, subjectId]));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Compensation_rate_cannot_be_negative()
    {
        var result = new CreateCompensationRuleRequestValidator()
            .Validate(new CreateCompensationRuleRequest(null, null, null, -1m, CompensationRateBasis.PerSession));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompensationRuleRequest.Rate));
    }

    [Fact]
    public void Tenant_default_rule_with_all_null_dimensions_is_valid()
    {
        var result = new CreateCompensationRuleRequestValidator()
            .Validate(new CreateCompensationRuleRequest(null, null, null, 100m, CompensationRateBasis.PerHour));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void Update_rejects_a_negative_rate_only_when_supplied()
    {
        var validator = new UpdateCompensationRuleRequestValidator();

        Assert.False(validator.Validate(new UpdateCompensationRuleRequest(-5m, null, null)).IsValid);
        Assert.True(validator.Validate(new UpdateCompensationRuleRequest(null, null, null)).IsValid);
    }

    [Fact]
    public void Command_wrappers_compose_the_request_rules()
    {
        var validator = new CreateTeacherCommandValidator();

        var result = validator.Validate(new CreateTeacherCommand(NewAccount() with { Email = "" }));

        Assert.False(result.IsValid);

        var nullBody = validator.Validate(new CreateTeacherCommand(null!));

        Assert.False(nullBody.IsValid);
    }

    private static CreateTeacherRequest NewAccount()
        => new(
            EmployeeNumber: "T-001",
            Email: "new-teacher@example.test",
            Password: "Teach#123",
            FullName: "New Teacher",
            PhoneNumber: null,
            UserId: null,
            HireDate: null,
            SubjectIds: null);

    private static CreateTeacherRequest Link()
        => new(
            EmployeeNumber: null,
            Email: null,
            Password: null,
            FullName: null,
            PhoneNumber: null,
            UserId: Guid.NewGuid(),
            HireDate: null,
            SubjectIds: null);
}
