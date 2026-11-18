using System.Globalization;
using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Common.Messaging;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Learning;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Localization;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace EducationHub.Application.Features.Registration;

/// <summary>
/// Reads for the anonymous pre-registration form. The caller has no token, so there is no tenant
/// context and every query filter would hide every row: these reads bypass the filters and apply
/// the tenant resolved from the public slug explicitly. They are kept here, in one place, so the
/// bypass stays greppable - nothing else in the feature uses it.
/// </summary>
public sealed class PublicRegistrationReader(
    IRepository<Tenant> tenants,
    IRepository<GradeLevel> grades,
    IRepository<Subject> subjects,
    IRepository<RegistrationRequest> requests,
    IDateTimeService clock)
{
    /// <summary>The active centre with that slug, or null (unknown, deleted, inactive or expired).</summary>
    public async Task<Tenant?> FindOpenTenantAsync(string? slug, CancellationToken cancellationToken)
    {
        var normalized = Normalize.Optional(slug)?.ToLowerInvariant();

        if (normalized is null || normalized.Length > 100)
        {
            return null;
        }

        var tenant = await tenants.Query
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == normalized && !t.IsDeleted, cancellationToken)
            .ConfigureAwait(false);

        return tenant is not null && tenant.CanAccess(clock.UtcNow) ? tenant : null;
    }

    public Task<List<GradeLevel>> GradesAsync(Guid tenantId, CancellationToken cancellationToken)
        => grades.Query
            .IgnoreQueryFilters()
            .Where(g => g.TenantId == tenantId && !g.IsDeleted && g.IsActive)
            .OrderBy(static g => g.Order)
            .ThenBy(static g => g.Name)
            .ToListAsync(cancellationToken);

    public Task<List<Subject>> SubjectsAsync(Guid tenantId, CancellationToken cancellationToken)
        => subjects.Query
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && !s.IsDeleted && s.IsActive)
            .OrderBy(static s => s.SortOrder)
            .ThenBy(static s => s.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> HasPendingAsync(Guid tenantId, string phone, string studentName, CancellationToken cancellationToken)
        => requests.Query
            .IgnoreQueryFilters()
            .AnyAsync(
                r => r.TenantId == tenantId
                    && !r.IsDeleted
                    && r.Status == RegistrationRequestStatus.Pending
                    && r.GuardianPhone == phone
                    && r.StudentName == studentName,
                cancellationToken);
}

/// <summary>The centre's name and the choices for the public form.</summary>
public sealed record GetPublicRegistrationFormQuery(string TenantSlug) : IRequest<Result<PublicRegistrationFormDto>>;

public sealed class GetPublicRegistrationFormQueryHandler(PublicRegistrationReader reader)
    : IRequestHandler<GetPublicRegistrationFormQuery, Result<PublicRegistrationFormDto>>
{
    public async Task<Result<PublicRegistrationFormDto>> Handle(GetPublicRegistrationFormQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenant = await reader.FindOpenTenantAsync(request.TenantSlug, cancellationToken).ConfigureAwait(false);

        if (tenant is null)
        {
            return Result<PublicRegistrationFormDto>.Failure("PublicRegistration.NotFound", "This registration page does not exist.");
        }

        var grades = await reader.GradesAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
        var subjects = await reader.SubjectsAsync(tenant.Id, cancellationToken).ConfigureAwait(false);

        return Result<PublicRegistrationFormDto>.Success(new PublicRegistrationFormDto(
            tenant.Slug,
            tenant.Name,
            tenant.NameAr,
            [.. grades.Select(static g => new PublicOptionDto(g.Id, g.Name, g.NameAr))],
            [.. subjects.Select(static s => new PublicOptionDto(s.Id, s.Name, s.NameAr))]));
    }
}

/// <summary>Records a pending pre-registration request from the public form.</summary>
public sealed record SubmitRegistrationCommand(string TenantSlug, SubmitRegistrationRequest Request)
    : IRequest<Result<RegistrationSubmittedDto>>;

public sealed class SubmitRegistrationCommandHandler(
    PublicRegistrationReader reader,
    IRepository<RegistrationRequest> requests,
    IRepository<Notification> notifications,
    IUserAccessReader access,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    WhatsAppMessenger whatsApp,
    IStringLocalizer<LocalizationResource> localizer,
    ILogger<SubmitRegistrationCommandHandler> logger)
    : IRequestHandler<SubmitRegistrationCommand, Result<RegistrationSubmittedDto>>
{
    public async Task<Result<RegistrationSubmittedDto>> Handle(SubmitRegistrationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenant = await reader.FindOpenTenantAsync(command.TenantSlug, cancellationToken).ConfigureAwait(false);

        if (tenant is null)
        {
            return Result<RegistrationSubmittedDto>.Failure("PublicRegistration.NotFound", "This registration page does not exist.");
        }

        var body = command.Request;
        var received = Localize(LocalizationKeys.RegistrationReceived, "Your request has been received. The centre will contact you soon.");

        // Honeypot: a person never sees the field. The bot gets the normal answer and nothing is stored.
        if (!string.IsNullOrWhiteSpace(body.Website))
        {
            logger.LogInformation("Pre-registration for tenant {TenantId} dropped by the honeypot.", tenant.Id);
            return Result<RegistrationSubmittedDto>.Success(new RegistrationSubmittedDto(tenant.Name), received);
        }

        var phone = PhoneNumberFormat.ToCanonical(body.GuardianPhone)!;
        var studentName = Normalize.Required(body.StudentName, string.Empty);
        var guardianName = Normalize.Required(body.GuardianName, string.Empty);

        if (await reader.HasPendingAsync(tenant.Id, phone, studentName, cancellationToken).ConfigureAwait(false))
        {
            return Result<RegistrationSubmittedDto>.Failure(
                "RegistrationRequests.AlreadyExists",
                Localize(LocalizationKeys.RegistrationDuplicate, "A request for this student from this mobile number is already waiting for review."));
        }

        // Only choices that belong to this centre are kept; anything else is ignored.
        var grades = await reader.GradesAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
        var subjects = await reader.SubjectsAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
        var gradeId = body.GradeLevelId is { } g && grades.Any(x => x.Id == g) ? g : (Guid?)null;
        var chosen = body.SubjectIds ?? [];
        var subjectNames = subjects.Where(s => chosen.Contains(s.Id)).Select(static s => string.IsNullOrWhiteSpace(s.NameAr) ? s.Name : s.NameAr).ToList();

        var request = new RegistrationRequest
        {
            TenantId = tenant.Id,
            GuardianName = guardianName,
            GuardianPhone = phone,
            GuardianEmail = Normalize.Email(body.GuardianEmail),
            Relationship = body.Relationship,
            StudentName = studentName,
            BirthDate = body.BirthDate,
            GradeLevelId = gradeId,
            School = Normalize.Optional(body.School),
            PreferredSubjects = subjectNames.Count == 0 ? null : Truncate(string.Join(", ", subjectNames), 500),
            Notes = Normalize.Optional(body.Notes),
            Status = RegistrationRequestStatus.Pending,
            SourceIp = Truncate(currentUser.IpAddress, 64)
        };

        await requests.AddAsync(request, cancellationToken).ConfigureAwait(false);

        // Staff who can see the requests get an in-app notification, in the centre's language.
        var staff = await access
            .GetActiveUserIdsWithPermissionAsync(tenant.Id, PermissionConstants.RegistrationRequestsView, cancellationToken)
            .ConfigureAwait(false);

        var (title, details) = InCulture(tenant.DefaultLanguage, () => (
            Localize(LocalizationKeys.RegistrationNotificationTitle, "New registration request"),
            $"{studentName} - {guardianName} ({phone})"));

        foreach (var userId in staff)
        {
            await notifications.AddAsync(
                    new Notification { TenantId = tenant.Id, UserId = userId, Title = title, Body = details, Link = "/registration-requests" },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // After the save, best-effort, and only when the centre switched the confirmation on.
        await whatsApp
            .SendRegistrationReceivedAsync(phone, guardianName, studentName, tenant.NameAr ?? tenant.Name, cancellationToken)
            .ConfigureAwait(false);

        return Result<RegistrationSubmittedDto>.Success(new RegistrationSubmittedDto(tenant.Name), received);
    }

    private string Localize(string key, string fallback)
    {
        var value = localizer[key];
        return value.ResourceNotFound || string.IsNullOrWhiteSpace(value.Value) ? fallback : value.Value;
    }

    private static T InCulture<T>(string? language, Func<T> action)
    {
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(language) ? "ar" : language);
        }
        catch (CultureNotFoundException)
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar");
        }

        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static string? Truncate(string? value, int length)
        => value is null || value.Length <= length ? value : value[..length];
}
