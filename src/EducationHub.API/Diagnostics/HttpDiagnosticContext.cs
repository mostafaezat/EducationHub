using EducationHub.Application.Common.Interfaces;
using EducationHub.API.Middleware;
using Microsoft.AspNetCore.Http;

namespace EducationHub.API.Diagnostics;

/// <summary>
/// Per-request diagnostics, read from the HTTP context.
/// </summary>
/// <remarks>
/// <para>
/// Lives in the API layer because <c>IDiagnosticContext</c> is deliberately declared
/// without any ASP.NET Core type: the Application layer must be able to consume correlation
/// ids without depending on <c>Microsoft.AspNetCore.Http</c>.
/// </para>
/// <para>
/// The correlation id is read from <c>HttpContext.Items</c> rather than from a header
/// directly. <c>CorrelationIdMiddleware</c> has already decided the value by the time any
/// scoped service runs - it owns the policy of accepting an inbound id versus minting one -
/// so re-reading the header here would let the two disagree, and an audit row carrying a
/// different id from the response header makes a log search fail silently.
/// </para>
/// </remarks>
public sealed class HttpDiagnosticContext(IHttpContextAccessor httpContextAccessor) : IDiagnosticContext
{
    public string CorrelationId
    {
        get
        {
            var context = httpContextAccessor.HttpContext;

            if (context is not null
                && context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value)
                && value is string id
                && !string.IsNullOrWhiteSpace(id))
            {
                return id;
            }

            // Null before the middleware has run and outside a request entirely (seeding,
            // background jobs). A fixed literal rather than empty: audit rows have a
            // non-nullable column and an unsearchable empty string would defeat the point.
            return "no-correlation-id";
        }
    }

    public string? IpAddress
    {
        get
        {
            var context = httpContextAccessor.HttpContext;

            if (context is null)
            {
                return null;
            }

            var address = context.Connection.RemoteIpAddress?.ToString();

            return string.IsNullOrWhiteSpace(address) ? null : address;
        }
    }
}