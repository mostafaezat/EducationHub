using EducationHub.Application;
using EducationHub.Application.Features.Academic.Commands;
using EducationHub.Application.Features.Auth.Commands;
using EducationHub.Application.Features.Auth.DTOs;
using EducationHub.Application.Features.Roles.Commands;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Application.Features.Teachers.Commands;
using EducationHub.Application.Features.Users.Commands;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace EducationHub.Tests.Unit.Application;

/// <summary>
/// Regression coverage for audit defect P0-1: validators were registered only for the
/// <c>*Request</c> DTOs, but MediatR dispatches the <c>*Command</c> wrappers, so
/// <c>IValidator&lt;TCommand&gt;</c> resolved to nothing and the pipeline skipped validation.
/// </summary>
/// <remarks>
/// These tests resolve the validators from the real container produced by
/// <see cref="DependencyInjection.AddApplicationServices"/>, so they fail if a command's
/// validator is ever dropped from the assembly or the DTO one stops being composed.
/// </remarks>
public sealed class CommandValidatorRegistrationTests
{
    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void LoginCommand_has_a_registered_validator()
    {
        var provider = BuildProvider();

        Assert.NotNull(provider.GetService<IValidator<LoginCommand>>());
    }

    [Fact]
    public void Invalid_LoginCommand_fails_validation_through_the_registered_validator()
    {
        var provider = BuildProvider();
        var validator = provider.GetRequiredService<IValidator<LoginCommand>>();

        var result = validator.Validate(new LoginCommand(new LoginRequest(string.Empty, string.Empty)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains(nameof(LoginRequest.Email), StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.PropertyName.Contains(nameof(LoginRequest.Password), StringComparison.Ordinal));
    }

    [Fact]
    public void Valid_LoginCommand_passes_validation_through_the_registered_validator()
    {
        var provider = BuildProvider();
        var validator = provider.GetRequiredService<IValidator<LoginCommand>>();

        var result = validator.Validate(new LoginCommand(new LoginRequest("user@example.test", "Passw0rd#1")));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void Null_request_body_is_rejected_rather_than_throwing()
    {
        var provider = BuildProvider();
        var validator = provider.GetRequiredService<IValidator<LoginCommand>>();

        // A record constructed with a null Request must produce a validation failure, not a
        // NullReferenceException - the child adaptor skips null and the NotNull rule reports it.
        var result = validator.Validate(new LoginCommand(null!));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Representative_commands_across_features_have_registered_validators()
    {
        var provider = BuildProvider();

        Assert.NotNull(provider.GetService<IValidator<CreateTenantCommand>>());
        Assert.NotNull(provider.GetService<IValidator<CreateUserCommand>>());
        Assert.NotNull(provider.GetService<IValidator<CreateRoleCommand>>());
        Assert.NotNull(provider.GetService<IValidator<CreateGradeLevelCommand>>());
        Assert.NotNull(provider.GetService<IValidator<CreateTeacherCommand>>());
        Assert.NotNull(provider.GetService<IValidator<CreateCompensationRuleCommand>>());
    }
}
