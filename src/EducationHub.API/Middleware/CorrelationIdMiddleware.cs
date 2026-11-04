using Serilog.Context;

namespace EducationHub.API.Middleware;

/// <summary>
/// Gives every request a correlation id and echoes it back.
/// </summary>
/// <remarks>
/// Serilog's <see cref="LogContext"/> pushes the id onto the logging scope, so every log
/// line written during the request - including ones from EF Core and Identity - carries it
/// without any of them having to know about it. That is what makes a single user-reported
/// failure traceable across all three layers.
/// </remarks>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "EducationHub.CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var incoming = context.Request.Headers[HeaderName].ToString();

        var correlationId = string.IsNullOrWhiteSpace(incoming)
            ? Guid.NewGuid().ToString("N")
            : incoming.Trim();

        if (correlationId.Length > 100)
        {
            // Bounded because it is written to the log and echoed to the client; an
            // unbounded header value would be a cheap way to bloat the log store.
            correlationId = correlationId[..100];
        }

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context).ConfigureAwait(false);
        }
    }

    public static string Get(HttpContext context)
        => context.Items.TryGetValue(ItemKey, out var value) && value is string id ? id : string.Empty;
}