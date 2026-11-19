using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using EducationHub.API.Auth;
using EducationHub.API.Diagnostics;
using EducationHub.API.Localization;
using EducationHub.API.Middleware;
using EducationHub.Application;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Infrastructure;
using EducationHub.Infrastructure.Persistence;
using EducationHub.Infrastructure.Services;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Localization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

const string CorsPolicyName = "DefaultCors";

// Policy used only by the credential-entry endpoints ([EnableRateLimiting]). Every other
// endpoint is unrestricted: the tenant query filter isolates data, and a blanket limiter
// would let an attacker DoS a tenancy by exhausting its share of a shared IP.
const string AuthCredentialRateLimitPolicy = "auth-credential";
const string PhoneResetRateLimitPolicy = RateLimitPolicies.PhoneReset;
const string PublicRegistrationRateLimitPolicy = RateLimitPolicies.PublicRegistration;

// Serilog is configured first and before the host is built, so that failures during startup
// are logged too rather than only reaching the console.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .WriteTo.Console());

builder.Services.AddHttpContextAccessor();
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// WhatsApp: the provider is a configuration choice (None by default, Log in development, Meta
// for the WhatsApp Cloud API). Meta needs an HttpClient, which is why the sender is wired here.
builder.Services.Configure<global::EducationHub.Application.Common.Messaging.WhatsAppOptions>(
    builder.Configuration.GetSection(global::EducationHub.Application.Common.Messaging.WhatsAppOptions.SectionName));

// Not an interpolated string: `{global::X}` would read the `:` as the start of a format clause.
const string WhatsAppSection = global::EducationHub.Application.Common.Messaging.WhatsAppOptions.SectionName;
var whatsAppProvider = builder.Configuration[WhatsAppSection + ":Provider"] ?? "None";

if (string.Equals(whatsAppProvider, "Meta", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IWhatsAppSender, MetaWhatsAppSender>(client => client.Timeout = TimeSpan.FromSeconds(10));
}
else if (string.Equals(whatsAppProvider, "Log", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IWhatsAppSender, LogWhatsAppSender>();
}
else
{
    builder.Services.AddScoped<IWhatsAppSender, DisabledWhatsAppSender>();
}

// Registered after the Application layer, which declares the interface but cannot implement
// it: resolving correlation ids requires HttpContext, which the Application project must not
// reference. Fully qualified because Serilog contributes its own unrelated
// `Serilog.IDiagnosticContext` to this file's imports.
builder.Services.AddScoped<
    global::EducationHub.Application.Common.Interfaces.IDiagnosticContext,
    HttpDiagnosticContext>();

// The signing key is sourced from configuration/environment, never from appsettings
// (see JwtOptions.ReadSigningKey). Resolved and validated here, before the host is built,
// so a missing, short or copied-placeholder key fails at startup instead of on the first
// login attempt.
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("The 'Jwt' configuration section is missing.");

jwt.SigningKey = JwtOptions.ReadSigningKey(builder.Configuration) ?? jwt.SigningKey;
JwtOptions.ValidateSigningKey(jwt.SigningKey);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),

            // Default is five minutes, which would keep a revoked or deactivated account
            // working long after the fact. The token's own 15-minute exp governs instead.
            NameClaimType = EducationHubClaimTypes.UserId,

            // Must name the claim type the token actually carries. `JwtTokenService` writes
            // roles as `ClaimTypes.Role` (the long URI), and inbound mapping leaves an
            // already-long URI untouched, so asking for "role" would find no role claim and
            // every `[Authorize(Roles = ...)]` would deny - including PlatformAdmin, which
            // is why /api/tenants returned 403 for a caller who plainly had the role.
            RoleClaimType = ClaimTypes.Role
        };
    });

builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, PlatformAdminAuthorizationHandler>();

// One policy per permission, named exactly as the permission code. [Authorize(Policy =
// "Users.Create")] therefore reads as the thing it enforces, and adding a permission to the
// catalog does not require touching the authorization setup at all.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        RoleNames.PlatformAdmin,
        policy => policy
            .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new PlatformAdminRequirement()));

    foreach (var permission in PermissionConstants.GetAll())
    {
        options.AddPolicy(
            permission,
            policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission)));
    }
});

builder.Services
    .AddControllers(options =>
    {
        // Controllers return Result<T>. Treating a failure as a 200 with an error body would
        // make every client inspect the payload, and every client would eventually forget.
        // This maps the Result shape onto real status codes centrally instead.
        options.Filters.Add<ApiResultFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Scoped because they take DbContext, RoleManager and UserManager, all of which are scoped
// and per-tenant-state. DatabaseSeeder is the only type the initializer resolves; the rest
// are registered so it (and the tests) can compose or invoke them individually.
builder.Services.AddScoped<EducationHub.API.Seed.RoleSeeder>();
builder.Services.AddScoped<EducationHub.API.Seed.AdminUserSeeder>();
builder.Services.AddScoped<EducationHub.API.Seed.DemoTenantSeeder>();
builder.Services.AddScoped<EducationHub.API.Seed.AcademicSeeder>();
builder.Services.AddScoped<EducationHub.API.Seed.DemoTeacherSeeder>();
builder.Services.AddScoped<EducationHub.API.Seed.DemoStudentParentSeeder>();
builder.Services.AddScoped<EducationHub.API.Seed.DemoStudentProfileSeeder>();
builder.Services.AddScoped<EducationHub.API.Seed.DemoStaffSeeder>();
builder.Services.AddScoped<EducationHub.API.Seed.DemoOperationsSeeder>();
builder.Services.AddScoped<EducationHub.API.Seed.DemoRegistrationRequestSeeder>();
builder.Services.AddScoped<EducationHub.API.Seed.DevelopmentDataSeeder>();
builder.Services.AddScoped<EducationHub.API.Seed.DatabaseSeeder>();

builder.Services.AddProblemDetails();

// Fixed-window limiter keyed on the remote IP. Login is already lockout-protected per
// account; this raises the barrier for spraying many <unknown> credentials at once, where
// the lockout has no per-account state to fight with. The rejection is returned as a
// ProblemDetails 429 with an RFC 6585 Retry-After header.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = static (context, cancellationToken) =>
        new ValueTask(RateLimitResponses.WriteAsync(context.HttpContext, RateLimitResponses.RetryAfterSeconds, cancellationToken));

    // Phone password reset, per remote IP. The per-number budget is PhoneResetRateLimiter.
    options.AddPolicy(PhoneResetRateLimitPolicy, httpContext =>
    {
        var limits = httpContext.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value.PhoneReset;

        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { Window = limits.Window, PermitLimit = limits.PermitLimit, QueueLimit = 0 });
    });

    // Token refresh, per remote IP: its own generous budget, never the login one (QA-01).
    options.AddPolicy(RateLimitPolicies.Refresh, httpContext =>
    {
        var limits = httpContext.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value.Refresh;

        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { Window = limits.Window, PermitLimit = limits.PermitLimit, QueueLimit = 0 });
    });

    // Anonymous pre-registration form, per remote IP.
    options.AddPolicy(PublicRegistrationRateLimitPolicy, httpContext =>
    {
        var limits = httpContext.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value.PublicRegistration;

        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { Window = limits.Window, PermitLimit = limits.PermitLimit, QueueLimit = 0 });
    });

    options.AddPolicy(AuthCredentialRateLimitPolicy, httpContext =>
    {
        var limits = httpContext.RequestServices
            .GetRequiredService<IOptions<AuthRateLimitOptions>>()
            .Value;

        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                Window = limits.Window,
                PermitLimit = limits.PermitLimit,
                QueueLimit = 0
            });
    });
});

builder.Services.Configure<AuthRateLimitOptions>(
    builder.Configuration.GetSection(AuthRateLimitOptions.SectionName));
builder.Services.AddSingleton<PhoneResetRateLimiter>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "EducationHub API",
        Version = "v1",
        Description = "Multi-tenant tutoring platform API. Arabic is the default language."
    });

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token only - without the 'Bearer ' prefix."
    };

    options.AddSecurityDefinition("Bearer", scheme);

    // OpenAPI.NET 2.x resolves scheme references against the document being built, so the
    // requirement is supplied as a factory rather than a fixed instance.
    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer")] = []
    });
});

builder.Services.AddApiLocalization();

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

// Migrate and seed before serving traffic. Development only, and config-gated, because
// running migrations as a side effect of booting would let two instances race and would
// make an accidental production deploy able to alter the schema.
if (app.Environment.IsDevelopment()
    && app.Configuration.GetValue("Database:AutoMigrateOnStartup", false))
{
    await EducationHub.API.DatabaseInitializer.ApplyAsync(app.Services).ConfigureAwait(false);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseSerilogRequestLogging(options =>
    options.GetLevel = (httpContext, _, ex) =>
        ex is not null || httpContext.Response.StatusCode >= 500
            ? LogEventLevel.Error
            : LogEventLevel.Debug);

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();

// SPA static files. wwwroot only exists after `npm run build:publish` from admin/; without
// it both middlewares are a no-op and the API behaves exactly as before (dev proxy serves
// the SPA on :4200). Placed after the exception/correlation/security middlewares so HTML
// responses get the same headers and error handling, and before authentication because
// static assets are anonymous (the SPA sends its JWT only to /api requests; the
// must-change-password and tenant middlewares act only on authenticated principals).
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors(CorsPolicyName);

// Must be present for [EnableRateLimiting] endpoints; placed before authentication so the
// per-IP credential guard never pays the cost of, or depends on, a valid identity.
app.UseRateLimiter();

// Order matters. TenantResolutionMiddleware reads the authenticated principal, so it must
// run after UseAuthentication and before anything that touches the database.
app.UseAuthentication();

// After authentication so the culture provider can fall back to the token's stored
// PreferredLanguage claim, and before TenantResolutionMiddleware so every downstream writer
// (result filter, middleware, rate limiter) sees the resolved culture already applied.
app.UseRequestLocalization(LocalizationSetup.BuildRequestLocalizationOptions());

app.UseMiddleware<TenantResolutionMiddleware>();

// Before authorization: a forced-password-change caller may only reach the allowlisted
// paths, so the deny happens regardless of what policy the endpoint would otherwise apply.
app.UseMiddleware<MustChangePasswordMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
    .AllowAnonymous()
    .WithTags("Health");

app.MapGet("/health/ready", async (EducationHubDbContext db, CancellationToken cancellationToken) =>
        await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
            ? Results.Ok(new { status = "ready" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous()
    .WithTags("Health");

// SPA deep links (/users, /auth/reset-password?...). Registered only when the SPA has been
// published into wwwroot, and unknown /api, /health and /swagger paths are kept as real
// 404s rather than quietly returning the HTML shell. The nonfile route constraint already
// keeps unmatched asset requests (paths with a dot) off this endpoint.
var spaIndex = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");

if (File.Exists(spaIndex))
{
    app.MapFallback(async (HttpContext context) =>
    {
        // The fallback exists for client-side route navigation, which is GET/HEAD only; a
        // stray POST/PUT to an unknown path must stay a 404 instead of returning HTML.
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            return Results.NotFound();
        }

        var path = context.Request.Path;

        if (path.StartsWithSegments("/api")
            || path.StartsWithSegments("/health")
            || path.StartsWithSegments("/swagger"))
        {
            return Results.NotFound();
        }

        return Results.File(spaIndex, "text/html");
    });
}

app.Run();

/// <summary>Exposed so the integration test project can build a test host.</summary>
public partial class Program;