using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Auth;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Infrastructure.Persistence;
using EducationHub.Infrastructure.Persistence.Interceptors;
using EducationHub.Infrastructure.Persistence.Repositories;
using EducationHub.Infrastructure.Identity;
using EducationHub.Infrastructure.Services;
using EducationHub.Shared.Common.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EducationHub.Infrastructure;

/// <summary>Wires the Infrastructure layer into the DI container.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence, Identity and the infrastructure services.
    /// </summary>
    /// <remarks>
    /// Everything here is <b>scoped</b>, and that is not a default to accept casually: the
    /// tenant query filter, the audit interceptor and the cached tenant time zone are all
    /// per-request state. Registering any of them as a singleton would either leak one
    /// request's tenant into another or pin a DbContext for the lifetime of the process.
    /// </remarks>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // SigningKey is resolved separately because it must never come from appsettings (see
        // JwtOptions.ReadSigningKey): the section contributes Issuer/Audience, and the key
        // arrives via configuration or the EducationHub__Jwt__SigningKey environment variable.
        services.Configure<JwtOptions>(jwt =>
        {
            configuration.Bind(JwtOptions.SectionName, jwt);
            jwt.SigningKey = JwtOptions.ReadSigningKey(configuration) ?? jwt.SigningKey;
        });
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));

        services.AddDbContext<EducationHubDbContext>((provider, options) =>
        {
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql =>
                {
                    // Transient retries absorb a failover or a briefly blocked database without
                    // the caller seeing an error. They deliberately do NOT cover command
                    // execution: a retried insert that actually succeeded the first time would
                    // silently duplicate the row.
                    sql.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorNumbersToAdd: null);

                    sql.MigrationsAssembly(typeof(EducationHubDbContext).Assembly.FullName);
                });
        });

        // Invoked explicitly by EducationHubDbContext.SaveChanges rather than registered as
        // an EF interceptor: auditing needs to see the final change tracker, including the
        // tenant stamps applied in the same pass, so it has to run after those are written.
        services.AddScoped<AuditSaveChangesInterceptor>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Open generic, so every entity is served by the same filtered implementation. Per-
        // entity registrations would be an invitation to add an unfiltered one by accident.
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        services.AddScoped<IApplicationDbContext>(
            static provider => provider.GetRequiredService<EducationHubDbContext>());

        services.AddScoped<IUserAccessReader, UserAccessReader>();
        services.AddScoped<IAuthenticationRepository, AuthenticationRepository>();

        services.AddScoped<ITenantContext, HttpTenantContext>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IDateTimeService, TenantDateTimeService>();

        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();

        // Development only by intent: LogEmailSender writes messages to the logs because no
        // SMTP transport exists yet. Swapping in a real sender is a registration change only.
        services.AddScoped<IEmailSender, LogEmailSender>();
        services.AddScoped<IWebLinkBuilder, WebLinkBuilder>();

        services
            .AddIdentityCore<ApplicationUser>(ConfigureIdentity)
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<EducationHubDbContext>()
            .AddDefaultTokenProviders();

        // Role name uniqueness is per tenant (see TenantScopedRoleValidator). Identity's
        // default validator, which scopes it to the whole database, would otherwise make the
        // second tenant's TenantAdmin a duplicate of the first tenant's.
        services.Replace(
            ServiceDescriptor.Scoped<IRoleValidator<ApplicationRole>, TenantScopedRoleValidator>());

        // Same defect one layer deeper: the stock UserStore resolves a role by normalised
        // name with a bare SingleOrDefault, which assumes globally unique names. Under the
        // platform scope that matches every tenant's TenantAdmin and throws ("Sequence
        // contains more than one element" -> HTTP 409) on every role assignment. The
        // replacement scopes each lookup to the tenant of the user being modified.
        // Replace() removes the IUserStore descriptor AddEntityFrameworkStores just
        // registered and appends ours, so the container resolves the override.
        services.Replace(
            ServiceDescriptor.Scoped<IUserStore<ApplicationUser>, ApplicationUserStore>());

        return services;
    }

    /// <summary>
    /// Identity policy.
    /// </summary>
    /// <remarks>
    /// Identity owns hashing, lockout and role storage; none of it is reimplemented here.
    /// The claim types below are the important part: they must agree with
    /// <see cref="EducationHubClaimTypes"/>, or <c>IsInRole</c> and the tenant check would
    /// read different claims out of the same token.
    /// </remarks>
    private static void ConfigureIdentity(IdentityOptions options)
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.SignIn.RequireConfirmedEmail = false;

        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = AuthConstants.FailedLoginLockoutThreshold;
        options.Lockout.DefaultLockoutTimeSpan = AuthConstants.FailedLoginLockoutDuration;

        options.User.RequireUniqueEmail = true;

        options.ClaimsIdentity.UserNameClaimType = EducationHubClaimTypes.UserId;
        options.ClaimsIdentity.UserIdClaimType = EducationHubClaimTypes.UserId;
        options.ClaimsIdentity.RoleClaimType = "role";
    }
}