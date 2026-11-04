using EducationHub.Shared.Common.Localization;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace EducationHub.Application.Common.Validation;

/// <summary>
/// Base validator that resolves messages via <see cref="IStringLocalizer{LocalizationResource}"/> when
/// injected, falling back to an explicitly provided literal when the localizer is not available.
/// </summary>
/// <remarks>
/// This keeps existing unit tests working (direct <c>new()</c> without DI) while enabling
/// localization in production when the container injects the localizer.
/// </remarks>
public abstract class LocalizedValidator<T> : AbstractValidator<T>
{
    private readonly IStringLocalizer<LocalizationResource>? _localizer;

    protected LocalizedValidator(IStringLocalizer<LocalizationResource>? localizer = null)
    {
        _localizer = localizer;
    }

    protected string Message(string key, string fallback)
    {
        if (_localizer is null)
        {
            return fallback;
        }

        var result = _localizer[key];
        if (result.ResourceNotFound || string.IsNullOrWhiteSpace(result.Value))
        {
            return fallback;
        }

        return result.Value;
    }
}