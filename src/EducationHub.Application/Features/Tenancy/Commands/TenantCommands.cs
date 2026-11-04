using EducationHub.Application.Common.Exceptions;
using EducationHub.Application.Common.Extensions;
using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Tenancy.DTOs;
using EducationHub.Application.Features.Tenancy.Services;
using EducationHub.Application.Features.Users.Commands;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Tenancy.Commands;

/// <summary>
/// Provisions a new tenant: its settings row, its seven system roles with permission claims,
/// and its first administrator account.
/// </summary>
/// <remarks>
/// <para>
/// <b>Platform-level operation.</b> <see cref="Tenant"/> is not tenant-owned, so no query
/// filter restricts it and the handler must do its own authorization: it requires platform
/// scope, which only a <c>PlatformAdmin</c> can ever be granted.
/// </para>
/// <para>
/// Everything above runs in one explicit transaction. A tenant that is visible before its
/// roles and administrator exist would be a trap for every later request, so any partial
/// failure rolls the whole thing back and reaches a tenant that can never be "almost" there.
/// UserManager and RoleManager write through the same scoped DbContext as the repositories,
/// which is what lets the intermediate saves they make themselves join the transaction.
/// </para>
/// </remarks>
public sealed record CreateTenantCommand(CreateTenantRequest Request) : IRequest<Result<TenantDto>>;

public sealed class CreateTenantCommandHandler(
    IRepository<Tenant> tenants,
    IRepository<TenantSettings> tenantSettings,
    IRepository<IdentityUserRole<Guid>> userRoles,
    TenantRoleProvisioner roleProvisioner,
    UserManager<ApplicationUser> userManager,
    ITenantContext tenantContext,
    IDateTimeService dateTime,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateTenantCommand, Result<TenantDto>>
{
    public async Task<Result<TenantDto>> Handle(CreateTenantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!tenantContext.IsPlatformScope)
        {
            throw new BusinessRuleException(
                "Tenants.Forbidden",
                "Only a platform administrator can create a tenant.");
        }

        var request = command.Request;
        var slug = Normalize.Slug(request.Slug);

        var slugTaken = await tenants.Query
            .AnyAsync(t => t.Slug == slug, cancellationToken)
            .ConfigureAwait(false);

        if (slugTaken)
        {
            return Result<TenantDto>.Failure(
                "Tenants.SlugTaken",
                $"The slug '{slug}' is already in use.");
        }

        // Email is globally unique: the same address must not sign up twice across tenants.
        // Checked here so the eventual Identity error never surfaces as a generic one.
        var normalizedAdminEmail = Normalize.Email(request.AdminEmail) ?? string.Empty;

        if (await userManager.FindByEmailAsync(normalizedAdminEmail).ConfigureAwait(false) is not null)
        {
            return Result<TenantDto>.Failure(
                "Tenants.AdminEmailTaken",
                $"The admin email '{normalizedAdminEmail}' is already in use.");
        }

        var tenant = new Tenant
        {
            Slug = slug,
            Name = Normalize.Required(request.Name, "Unnamed tenant"),
            NameAr = Normalize.Optional(request.NameAr),
            Description = Normalize.Optional(request.Description),
            DescriptionAr = Normalize.Optional(request.DescriptionAr),
            Email = Normalize.Email(request.Email),
            Phone = Normalize.Optional(request.Phone),
            Address = Normalize.Optional(request.Address),
            AddressAr = Normalize.Optional(request.AddressAr),
            City = Normalize.Optional(request.City),
            Country = Normalize.Optional(request.Country),
            Type = request.Type,
            Status = TenantStatus.Active,
            Currency = Normalize.Required(request.Currency, "EGP").ToUpperInvariant(),
            TimeZone = Normalize.Required(request.TimeZone, "Africa/Cairo"),
            DefaultLanguage = Normalize.Required(request.DefaultLanguage, "ar"),
            IsActive = true,
            SubscriptionStartDate = dateTime.UtcNow
        };

        // Created in the same unit of work so a tenant is never visible without its
        // configuration. TenantSettings.TenantId is the shared primary key, which makes the
        // 1:1 relationship structural rather than conventional.
        var settings = new TenantSettings
        {
            TenantId = tenant.Id,
            CurrencySymbol = tenant.Currency,
            DateFormat = "dd/MM/yyyy"
        };

        var admin = new ApplicationUser
        {
            TenantId = tenant.Id,
            FullName = Normalize.Required(request.AdminFullName, string.Empty),
            Email = normalizedAdminEmail,
            UserName = normalizedAdminEmail,
            PreferredLanguage = tenant.DefaultLanguage,
            MustChangePassword = false,
            IsActive = true,
            EmailConfirmed = true
        };

        // Everything from the first row to the last commit runs inside one transaction, so a
        // tenant is never visible before its roles and administrator exist. UserManager and
        // RoleManager write through the same scoped DbContext as the repositories, which is
        // what lets the intermediate saves they make internally join the transaction.
        return await unitOfWork
            .ExecuteInTransactionAsync(ct => ProvisionAsync(tenant, settings, admin, request.AdminPassword, ct), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<TenantDto>> ProvisionAsync(
        Tenant tenant,
        TenantSettings settings,
        ApplicationUser admin,
        string adminPassword,
        CancellationToken cancellationToken)
    {
        await tenants.AddAsync(tenant, cancellationToken).ConfigureAwait(false);
        await tenantSettings.AddAsync(settings, cancellationToken).ConfigureAwait(false);

        var roleErrors = new List<string>();

        foreach (var roleName in RoleNames.TenantRoles)
        {
            var provisioned = await roleProvisioner
                .EnsureRoleAsync(roleName, tenant, cancellationToken)
                .ConfigureAwait(false);

            if (!provisioned.Succeeded)
            {
                roleErrors.AddRange(provisioned.Errors);
            }
        }

        if (roleErrors.Count > 0)
        {
            return Result<TenantDto>.Failure(
                "Tenants.RoleProvisioningFailed",
                "The tenant's default roles could not be created: "
                + string.Join("; ", roleErrors));
        }

        var created = await userManager
            .CreateAsync(admin, adminPassword)
            .ConfigureAwait(false);

        if (!created.Succeeded)
        {
            return Result<TenantDto>.Failure(
                UserCommandSupport.IdentityErrorsToErrors(created),
                "The tenant could not be created because the administrator account was rejected.");
        }

        // Membership written by role id rather than through AddToRoleAsync: that Identity API
        // resolves the role name against the whole role table, and with a TenantAdmin per
        // tenant several rows share NormalizedName - it throws on the ambiguity. The role
        // was just provisioned above, so this tenant's copy is known exactly.
        var tenantAdminRole = await roleProvisioner
            .FindRoleAsync(RoleNames.TenantAdmin, tenant.Id, cancellationToken)
            .ConfigureAwait(false);

        if (tenantAdminRole is null)
        {
            return Result<TenantDto>.Failure(
                "Tenants.TenantAdminRoleMissing",
                "The administrator's TenantAdmin role could not be found after provisioning.");
        }

        await userRoles
            .AddAsync(
                new IdentityUserRole<Guid>
                {
                    UserId = admin.Id,
                    RoleId = tenantAdminRole.Id
                },
                cancellationToken)
            .ConfigureAwait(false);

        // Tenant and settings were already flushed by CreateAsync's own save; this catches
        // nothing in practice but keeps the explicit "save then rollback-aware" shape.
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<TenantDto>.Success(
            TenantDto.FromEntity(tenant, dateTime.UtcNow, admin.Email),
            $"Tenant created successfully. Its administrator is {admin.Email}.");
    }
}

/// <summary>Updates a tenant's profile and subscription dates.</summary>
public sealed record UpdateTenantCommand(Guid Id, UpdateTenantRequest Request) : IRequest<Result<TenantDto>>;

public sealed class UpdateTenantCommandHandler(
    IRepository<Tenant> tenants,
    ITenantContext tenantContext,
    IDateTimeService dateTime,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateTenantCommand, Result<TenantDto>>
{
    public async Task<Result<TenantDto>> Handle(UpdateTenantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenant = await tenants.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (tenant is null)
        {
            return Result<TenantDto>.Failure("Tenants.NotFound", "Tenant not found.");
        }

        // A tenant administrator may edit their own tenant; a platform administrator may
        // edit any. Both cases are checked here because Tenant carries no query filter to
        // do it for us.
        if (!tenantContext.IsPlatformScope && tenantContext.TenantId != tenant.Id)
        {
            // Reported as "not found" rather than "forbidden" so the endpoint cannot be used
            // to discover which tenant ids exist.
            return Result<TenantDto>.Failure("Tenants.NotFound", "Tenant not found.");
        }

        var request = command.Request;

        tenant.Name = Normalize.Optional(request.Name) ?? tenant.Name;
        tenant.NameAr = Normalize.Optional(request.NameAr) ?? tenant.NameAr;
        tenant.Description = Normalize.Optional(request.Description) ?? tenant.Description;
        tenant.DescriptionAr = Normalize.Optional(request.DescriptionAr) ?? tenant.DescriptionAr;
        tenant.Email = Normalize.Email(request.Email) ?? tenant.Email;
        tenant.Phone = Normalize.Optional(request.Phone) ?? tenant.Phone;
        tenant.Address = Normalize.Optional(request.Address) ?? tenant.Address;
        tenant.AddressAr = Normalize.Optional(request.AddressAr) ?? tenant.AddressAr;
        tenant.City = Normalize.Optional(request.City) ?? tenant.City;
        tenant.Country = Normalize.Optional(request.Country) ?? tenant.Country;
        tenant.Currency = Normalize.Optional(request.Currency)?.ToUpperInvariant() ?? tenant.Currency;
        tenant.TimeZone = Normalize.Optional(request.TimeZone) ?? tenant.TimeZone;
        tenant.DefaultLanguage = Normalize.Optional(request.DefaultLanguage) ?? tenant.DefaultLanguage;

        if (request.Status.HasValue || request.IsActive.HasValue)
        {
            // Only a platform administrator may change lifecycle state. Without this a
            // tenant administrator could re-activate a suspended subscription and restore
            // their own access.
            if (!tenantContext.IsPlatformScope)
            {
                throw new BusinessRuleException(
                    "Tenants.Forbidden",
                    "Only a platform administrator can change tenant status or activation.");
            }

            if (request.Status.HasValue)
            {
                tenant.Status = request.Status.Value;
            }

            if (request.IsActive.HasValue)
            {
                tenant.IsActive = request.IsActive.Value;
            }
        }

        if (request.SubscriptionStartDate.HasValue)
        {
            tenant.SubscriptionStartDate = request.SubscriptionStartDate.Value;
        }

        if (request.SubscriptionExpiryDate.HasValue)
        {
            tenant.SubscriptionExpiryDate = request.SubscriptionExpiryDate.Value;
        }

        if (tenant.SubscriptionStartDate.HasValue
            && tenant.SubscriptionExpiryDate.HasValue
            && tenant.SubscriptionExpiryDate < tenant.SubscriptionStartDate)
        {
            return Result<TenantDto>.Failure(
                "Tenants.InvalidSubscriptionRange",
                "The subscription expiry date cannot be earlier than its start date.");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<TenantDto>.Success(TenantDto.FromEntity(tenant, dateTime.UtcNow), "Tenant updated successfully.");
    }
}

/// <summary>Soft deletes a tenant. Platform-level operation.</summary>
public sealed record DeleteTenantCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteTenantCommandHandler(
    IRepository<Tenant> tenants,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteTenantCommand, Result>
{
    public async Task<Result> Handle(DeleteTenantCommand command, CancellationToken cancellationToken)
    {
        if (!tenantContext.IsPlatformScope)
        {
            throw new BusinessRuleException(
                "Tenants.Forbidden",
                "Only a platform administrator can delete a tenant.");
        }

        var tenant = await tenants.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (tenant is null)
        {
            return Result.Failure("Tenants.NotFound", "Tenant not found.");
        }

        // Soft delete, not hard delete: audit, login history and finance records for this
        // tenant must stay readable and provable after the tenant is gone.
        tenants.SoftRemove(tenant);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success("Tenant deleted successfully.");
    }
}
