using EducationHub.Shared.Common.Models;
using FluentValidation;
using MediatR;
using ValidationException = FluentValidation.ValidationException;

namespace EducationHub.Application.Common.Behaviors;

/// <summary>
/// Runs FluentValidation before the handler executes, and turns failures into a
/// validation <see cref="Result"/> instead of an exception.
/// </summary>
/// <remarks>
/// <para>
/// Validation is cross-cutting, so every handler gets it for free and no handler can
/// forget to invoke its own validator.
/// </para>
/// <para>
/// The response constraint is <c>where TResponse : Result</c>, which
/// <see cref="Result{T}"/> satisfies by inheritance. That inheritance is why
/// <see cref="Result{T}"/> derives from <see cref="Result"/> rather than standing alone.
/// </para>
/// </remarks>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (!validators.Any())
        {
            return await next().ConfigureAwait(false);
        }

        var context = new ValidationContext<TRequest>(request);

        var failures = new List<FluentValidation.Results.ValidationFailure>();

        foreach (var validator in validators)
        {
            var result = await validator
                .ValidateAsync(context, cancellationToken)
                .ConfigureAwait(false);

            failures.AddRange(result.Errors.Where(static f => f is not null));
        }

        if (failures.Count > 0)
        {
            // Thrown rather than returned so the exception middleware can produce the same
            // 400 shape whether validation failed here or inside the handler itself.
            throw new ValidationException(failures);
        }

        return await next().ConfigureAwait(false);
    }
}
