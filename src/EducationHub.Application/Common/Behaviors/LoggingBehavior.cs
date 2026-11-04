using System.Diagnostics;
using EducationHub.Application.Common.Exceptions;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EducationHub.Application.Common.Behaviors;

/// <summary>
/// Logs the start and end of every request, and warns when a handler exceeds the
/// slow-request threshold.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    ICurrentUser currentUser,
    IDiagnosticContext diagnostics)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Above this, the handler is logged as a warning so it surfaces in monitoring.</summary>
    private const int SlowRequestThresholdMs = 500;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(next);

        var requestName = typeof(TRequest).Name;

        logger.LogInformation(
            "Handling {RequestName} for user {UserId} (tenant {TenantId}) [correlation {CorrelationId}]",
            requestName,
            currentUser.UserId,
            currentUser.TenantId,
            diagnostics.CorrelationId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next().ConfigureAwait(false);

            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > SlowRequestThresholdMs)
            {
                logger.LogWarning(
                    "Slow request: {RequestName} took {ElapsedMs} ms [correlation {CorrelationId}]",
                    requestName,
                    stopwatch.ElapsedMilliseconds,
                    diagnostics.CorrelationId);
            }
            else
            {
                logger.LogInformation(
                    "Handled {RequestName} in {ElapsedMs} ms [correlation {CorrelationId}]",
                    requestName,
                    stopwatch.ElapsedMilliseconds,
                    diagnostics.CorrelationId);
            }

            return response;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            logger.LogError(
                exception,
                "Handler {RequestName} threw after {ElapsedMs} ms [correlation {CorrelationId}]",
                requestName,
                stopwatch.ElapsedMilliseconds,
                diagnostics.CorrelationId);

            throw;
        }
    }
}

/// <summary>
/// Converts a <see cref="BusinessRuleException"/> raised by a handler into a failed
/// <see cref="Result"/> with the same shape as any other error response.
/// </summary>
/// <remarks>
/// Anything that is <b>not</b> a <see cref="BusinessRuleException"/> is deliberately
/// rethrown, so unexpected faults reach the global exception middleware and surface as a
/// <c>500</c> with a correlation id rather than being flattened into a tidy-looking
/// business error.
/// </remarks>
public sealed class BusinessRuleExceptionBehavior<TRequest, TResponse>(
    ILogger<BusinessRuleExceptionBehavior<TRequest, TResponse>> logger)
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

        try
        {
            return await next().ConfigureAwait(false);
        }
        catch (BusinessRuleException exception)
        {
            logger.LogWarning(
                "Business rule '{Code}' rejected {RequestName}: {Reason}",
                exception.Code,
                typeof(TRequest).Name,
                exception.Message);

            // ResultFactory resolves the closed generic type once and caches the compiled
            // delegate, so this is a cast and a delegate call at request time.
            return ResultFactory.Failure<TResponse>(exception.Message, exception.Errors);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client disconnected or the request timed out: not a business outcome.
            throw;
        }
    }
}
