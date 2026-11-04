using System.Text.Json;
using EducationHub.API.Localization;
using EducationHub.Application.Common.Exceptions;
using EducationHub.Shared.Common.Localization;
using EducationHub.Shared.Common.Models;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.API.Middleware;

/// <summary>
/// Translates exceptions into RFC 7807 problem responses.
/// </summary>
/// <remarks>
/// Without this, an unexpected exception returns ASP.NET Core's default response: an HTML
/// error page, or a stack trace under <c>UseDeveloperExceptionPage</c>. Neither is
/// acceptable for a JSON API, and neither should reveal internals in production.
/// </remarks>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment environment,
    JsonResourceStore store)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (BusinessRuleException ex)
        {
            // An expected, already-classified failure. No stack trace, no error log: it is
            // part of normal operation and logging it would bury real incidents.
            logger.LogInformation(
                "Business rule rejected {Method} {Path}: {Code} - {Message}",
                context.Request.Method,
                context.Request.Path,
                ex.Code,
                ex.Message);

            await WriteAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    ex.Code,
                    ex.Message,
                    ex.Errors)
                .ConfigureAwait(false);
        }
        catch (ValidationException ex)
        {
            // FluentValidation rejected a malformed request before any handler ran. That is a
            // client error, so it is a 400 - not the 500 the generic handler below would
            // otherwise produce. Field names drop the command's "Request." prefix so the
            // client sees the DTO property it actually sent.
            var errors = ex.Errors
                .Where(static failure => failure is not null)
                .Select(static failure => new Error(ToFieldName(failure.PropertyName), failure.ErrorMessage))
                .ToArray();

            logger.LogInformation(
                "Validation rejected {Method} {Path}: {Count} error(s)",
                context.Request.Method,
                context.Request.Path,
                errors.Length);

            await WriteAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    "Errors.Validation",
                    BoundaryMessages.Resolve(
                        store,
                        context,
                        LocalizationKeys.ErrorValidation,
                        "One or more validation errors occurred."),
                    errors)
                .ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Unauthorized access to {Method} {Path}", context.Request.Method, context.Request.Path);

            await WriteAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    "Auth.Forbidden",
                    BoundaryMessages.Resolve(
                        store,
                        context,
                        LocalizationKeys.AuthForbidden,
                        "You do not have permission to perform this action."))
                .ConfigureAwait(false);
        }
        catch (KeyNotFoundException ex)
        {
            await WriteAsync(
                    context,
                    StatusCodes.Status404NotFound,
                    "Errors.NotFound",
                    ex.Message)
                .ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Another request changed the same row first (optimistic concurrency on money
            // rows such as charges). Nothing was saved; the client can reload and retry.
            logger.LogWarning(ex, "Concurrency conflict handling {Method} {Path}", context.Request.Method, context.Request.Path);

            await WriteAsync(
                    context,
                    StatusCodes.Status409Conflict,
                    "Errors.ConcurrencyConflict",
                    "The record was changed by another request. Reload and try again.")
                .ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A unique index rejected a duplicate (e.g. a second cash posting for the same
            // source, or a repeated generated charge). The whole save was rolled back.
            logger.LogWarning(ex, "Duplicate rejected by a unique index handling {Method} {Path}", context.Request.Method, context.Request.Path);

            await WriteAsync(
                    context,
                    StatusCodes.Status409Conflict,
                    "Errors.Duplicate",
                    "The request duplicates an existing record and was not applied.")
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            // Save-time isolation violations land here. They are our own invariant breaking,
            // so they are logged loudly but still reported generically: the detail names
            // entities and tenant ids, which is exactly what an attacker probes for.
            logger.LogError(
                ex,
                "Invariant violation handling {Method} {Path}",
                context.Request.Method,
                context.Request.Path);

            await WriteAsync(
                    context,
                    StatusCodes.Status409Conflict,
                    "Errors.Conflict",
                    environment.IsDevelopment()
                        ? ex.Message
                        : BoundaryMessages.Resolve(
                            store,
                            context,
                            LocalizationKeys.ErrorConflict,
                            "The request could not be completed."))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client hung up. Not an error, and nothing useful to send.
            logger.LogInformation("Request {Method} {Path} aborted by the client", context.Request.Method, context.Request.Path);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unhandled exception handling {Method} {Path}",
                context.Request.Method,
                context.Request.Path);

            await WriteAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "Errors.Internal",
                    environment.IsDevelopment()
                        ? ex.ToString()
                        : BoundaryMessages.Resolve(
                            store,
                            context,
                            LocalizationKeys.ErrorInternal,
                            "An unexpected error occurred. Please try again."))
                .ConfigureAwait(false);
        }
    }

    /// <summary>Prefix FluentValidation adds when validating a command's wrapped request DTO.</summary>
    private const string WrappedRequestPrefix = "Request.";

    private static string ToFieldName(string propertyName)
        => propertyName.StartsWith(WrappedRequestPrefix, StringComparison.Ordinal)
            ? propertyName[WrappedRequestPrefix.Length..]
            : propertyName;

    private static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string detail,
        IReadOnlyList<Error>? errors = null)
    {
        if (context.Response.HasStarted)
        {
            // Headers are already on the wire; rewriting the body would produce a malformed
            // response, so the only honest thing left is to log (already done) and abort.
            context.Abort();

            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = code,
            Detail = detail
        };

        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        if (errors is { Count: > 0 })
        {
            problem.Extensions["errors"] = errors;
        }

        await context.Response
            .WriteAsync(JsonSerializer.Serialize(problem, JsonOptions))
            .ConfigureAwait(false);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}