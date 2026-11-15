using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Application.Features.Tenancy.DTOs;
using EducationHub.Domain.Enums;
using EducationHub.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EducationHub.API.Seed;

/// <summary>
/// Creates the <c>admin-ui-test</c> demo tenant and its administrator on a fresh development
/// database, so a brand-new database is usable without first clicking through the platform UI.
/// </summary>
/// <remarks>
/// <para>
/// Goes through <see cref="CreateTenantCommand"/> - the exact path <c>POST /api/tenants</c> takes
/// (validation, settings, the seven tenant roles, the admin account) - rather than writing rows
/// by hand. Runs under the platform scope the seed pipeline already holds.
/// </para>
/// <para>
/// Opt-in and add-only, like the other demo seeders: without
/// <c>EDUCATIONHUB_DEMO_TENANT_ADMIN_PASSWORD</c> it logs a warning and creates nothing; when the
/// tenant already exists (by slug, even soft-deleted) it does nothing and never touches its
/// admin's password. The local launch profile sets the variables; nothing else does.
/// </para>
/// </remarks>
public sealed class DemoTenantSeeder(
    EducationHubDbContext context,
    IMediator mediator,
    ILogger<DemoTenantSeeder> logger)
{
    public const string AdminEmailEnvironmentVariable = "EDUCATIONHUB_DEMO_TENANT_ADMIN_EMAIL";
    public const string AdminPasswordEnvironmentVariable = "EDUCATIONHUB_DEMO_TENANT_ADMIN_PASSWORD";
    public const string DefaultAdminEmail = "tenant.admin@educationhub.local";

    public Task SeedAsync(CancellationToken cancellationToken = default)
        => SeedAsync(
            AcademicSeeder.DemoTenantSlug,
            Environment.GetEnvironmentVariable(AdminEmailEnvironmentVariable) is { Length: > 0 } email ? email : DefaultAdminEmail,
            Environment.GetEnvironmentVariable(AdminPasswordEnvironmentVariable),
            cancellationToken);

    public async Task SeedAsync(string slug, string adminEmail, string? adminPassword, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminEmail);

        var exists = await context.Tenants.IgnoreQueryFilters()
            .AnyAsync(t => t.Slug == slug, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            logger.LogDebug("Demo tenant {Slug} already exists; nothing to create.", slug);
            return;
        }

        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            logger.LogWarning(
                "{Variable} is not set; the demo tenant {Slug} was not created. Set it (the local launch profile does) and restart.",
                AdminPasswordEnvironmentVariable,
                slug);
            return;
        }

        var result = await mediator.Send(
                new CreateTenantCommand(new CreateTenantRequest(
                    Slug: slug,
                    Name: "EducationHub Demo Center",
                    NameAr: "مركز EducationHub التجريبي",
                    Type: TenantType.EducationCenter,
                    Email: adminEmail,
                    Phone: null,
                    Address: null,
                    AddressAr: null,
                    City: "Cairo",
                    Country: "EG",
                    Currency: "EGP",
                    TimeZone: "Africa/Cairo",
                    DefaultLanguage: "ar",
                    Description: "Demo tenant created by the development seed.",
                    DescriptionAr: null,
                    AdminFullName: "Demo Tenant Admin",
                    AdminEmail: adminEmail,
                    AdminPassword: adminPassword)),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            logger.LogInformation("Created demo tenant {Slug} with administrator {Email}.", slug, adminEmail);
        }
        else
        {
            logger.LogWarning(
                "Demo tenant {Slug} was not created: {Message} {Errors}",
                slug,
                result.Message,
                string.Join("; ", result.Errors.Select(static e => e.Message)));
        }
    }
}
