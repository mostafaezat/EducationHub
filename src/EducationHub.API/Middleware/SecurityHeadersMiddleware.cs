namespace EducationHub.API.Middleware;

/// <summary>
/// Adds the baseline security response headers to every response.
/// </summary>
/// <remarks>
/// <para>
/// Placed first in the pipeline so the headers are present on error responses too: a 500 or a
/// 403 built by the exception handler is exactly where a browser would otherwise apply its
/// own content sniffing and framing defaults.
/// </para>
/// <para>
/// HSTS is emitted only when the request arrived over HTTPS - over plain HTTP the browser
/// would ignore it anyway, and setting it would suggest a guarantee the deployment does not
/// yet have.
/// </para>
/// <para>
/// The Content-Security-Policy is classified by path because this host serves three distinct
/// kinds of response: JSON endpoints (/api, /health), the Swagger UI page, and - when the
/// Angular admin client has been published into wwwroot - the SPA's HTML and static assets.
/// A blanket <c>default-src 'none'</c> (the policy that fits bare JSON) would block every
/// script, style, font and fetch of the SPA it is served alongside, so each class gets the
/// minimum policy it actually needs.
/// </para>
/// </remarks>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Applied to the SPA document and its assets (anything that is not /api, /health or
    /// /swagger). Scripts stay strict (no <c>unsafe-eval</c>, no <c>unsafe-inline</c>, no
    /// wildcards):
    /// <list type="bullet">
    /// <item><description><c>script-src 'self'</c> - the production build emits only external,
    /// hashed bundles. Critical-CSS inlining is disabled in the production build
    /// (<c>optimization.styles.inlineCritical: false</c>) precisely so that no inline
    /// &lt;style&gt; or inline event handler is injected into the document.</description></item>
    /// <item><description><c>style-src 'self' 'unsafe-inline'</c> - external stylesheets are
    /// self-hosted, and <c>'unsafe-inline'</c> is genuinely required because Angular injects
    /// &lt;style&gt; elements at runtime for lazily loaded component styles and for the
    /// PrimeNG theme; there is no nonce/hash-based way to cover dynamically generated
    /// stylesheets. This relaxes styles only - scripts remain locked down.</description></item>
    /// <item><description><c>connect-src 'self'</c> - API calls are same-origin; no websocket
    /// is needed here because the Vite dev server (live reload) serves its own document
    /// without this header, and the published build has no dev server.</description></item>
    /// <item><description><c>img-src 'self' data:</c> - self-hosted favicon/icons plus the
    /// occasional inline data URI.</description></item>
    /// </list>
    /// </summary>
    private const string SpaContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    /// <summary>JSON endpoints: nothing is loaded from a response, so nothing may be.</summary>
    private const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var path = context.Request.Path;

        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";

        if (path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            // Swagger UI is a real HTML page with scripts and styles; it is reachable only in
            // Development and is left without a CSP so the page can render at all.
        }
        else if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            headers["Content-Security-Policy"] = ApiContentSecurityPolicy;
        }
        else
        {
            // SPA document and static assets served from wwwroot (single-port mode).
            headers["Content-Security-Policy"] = SpaContentSecurityPolicy;
        }

        if (context.Request.IsHttps)
        {
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        }

        await next(context).ConfigureAwait(false);
    }
}