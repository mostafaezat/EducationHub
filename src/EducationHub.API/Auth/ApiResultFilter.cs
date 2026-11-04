using EducationHub.API.Localization;
using EducationHub.Shared.Common.Localization;
using EducationHub.Shared.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EducationHub.API.Auth;

/// <summary>
/// Maps the Application layer's <see cref="Result"/> onto real HTTP status codes.
/// </summary>
/// <remarks>
/// <para>
/// This is the single place the mapping happens, which is the point: it means a handler
/// never has to know about HTTP, and no endpoint can accidentally return
/// <c>200 OK</c> with a failure payload. The alternative - each controller inspecting
/// <c>result.IsFailure</c> - is exactly the kind of repetition that produces one endpoint
/// with the bug.
/// </para>
/// <para>
/// The mapping is by convention, not by a lookup table on error codes, because codes are
/// product-specific and grow constantly:
/// </para>
/// <list type="bullet">
/// <item><c>*NotFound*</c> → 404, <c>*Forbidden*</c> → 403, <c>*Unauthorized*</c> → 401</item>
/// <item><c>*Conflict</c> / <c>*Taken</c> / <c>*InUse</c> / <c>*AlreadyExists*</c> → 409</item>
/// <item><c>*Inactive</c> / <c>*Expired</c> / <c>*Archived</c> / <c>*Locked*</c> → 409</item>
/// <item>anything else → 400</item>
/// </list>
/// <para>
/// That last default is deliberate. A handler returning a failure means the request could not
/// be satisfied as asked, and 400 is the honest answer. 500 is reserved for faults the code
/// did not anticipate.
/// </para>
/// </remarks>
public sealed class ApiResultFilter(JsonResourceStore store) : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.Result is not ObjectResult objectResult)
        {
            await next().ConfigureAwait(false);

            return;
        }

        if (objectResult.Value is not Result result)
        {
            await next().ConfigureAwait(false);

            return;
        }

        if (!result.IsFailure)
        {
            await next().ConfigureAwait(false);

            return;
        }

        var code = result.Errors.Count > 0 ? result.Errors[0].Code : LocalizationKeys.ErrorFailed;
        var fallback = result.Message ?? (result.Errors.Count > 0 ? result.Errors[0].Message : "The request failed.");

        var problem = new ProblemDetails
        {
            Status = MapStatus(result),
            Title = result.Errors.Count > 0 ? result.Errors[0].Code : LocalizationKeys.ErrorFailed,

            // A message the handler set explicitly is already the intended wording, so it is
            // kept as-is. Only the generic fallback is resolved through the catalog.
            Detail = result.Message ?? BoundaryMessages.Resolve(store, context.HttpContext, code, fallback)
        };

        problem.Extensions["code"] = code;
        problem.Extensions["errors"] = result.Errors;

        if (context.HttpContext.Items.TryGetValue(
                Middleware.CorrelationIdMiddleware.ItemKey,
                out var correlationId)
            && correlationId is string id)
        {
            problem.Extensions["traceId"] = id;
        }

        context.Result = new ObjectResult(problem) { StatusCode = problem.Status };

        await next().ConfigureAwait(false);
    }

    private static int MapStatus(Result result)
    {
        var code = result.Errors.Count > 0 ? result.Errors[0].Code : string.Empty;

        if (string.IsNullOrEmpty(code))
        {
            return StatusCodes.Status400BadRequest;
        }

        if (code.Contains("NotFound", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCodes.Status404NotFound;
        }

        if (code.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)
            || code.Contains("EmailNotConfirmed", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCodes.Status403Forbidden;
        }

        if (code.Contains("Unauthenticated", StringComparison.OrdinalIgnoreCase)
            || code.Contains("InvalidCredentials", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCodes.Status401Unauthorized;
        }

        if (code.Contains("Conflict", StringComparison.OrdinalIgnoreCase)
            || code.Contains("Taken", StringComparison.OrdinalIgnoreCase)
            || code.Contains("InUse", StringComparison.OrdinalIgnoreCase)
            || code.Contains("AlreadyExists", StringComparison.OrdinalIgnoreCase)
            || code.Contains("Inactive", StringComparison.OrdinalIgnoreCase)
            || code.Contains("Expired", StringComparison.OrdinalIgnoreCase)
            || code.Contains("Archived", StringComparison.OrdinalIgnoreCase)
            || code.Contains("Locked", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCodes.Status409Conflict;
        }

        return StatusCodes.Status400BadRequest;
    }
}