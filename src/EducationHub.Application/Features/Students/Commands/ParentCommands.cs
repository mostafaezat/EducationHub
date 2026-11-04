using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Students.DTOs;
using EducationHub.Application.Features.Students.Services;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Domain.Entities.Students;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Students.Commands;

/// <summary>
/// Creates a parent profile and, atomically, optionally links or creates its account
/// (the new account signs in with the parent's e-mail and gets the Parent role).
/// </summary>
public sealed record CreateParentCommand(CreateParentRequest Request) : IRequest<Result<ParentProfileDto>>;

public sealed class CreateParentCommandHandler(
    IRepository<ParentProfile> parents,
    ProfileAccountSupport accounts,
    StudentReadModel readModel,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateParentCommand, Result<ParentProfileDto>>
{
    public async Task<Result<ParentProfileDto>> Handle(
        CreateParentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await unitOfWork.ExecuteInTransactionAsync(
            async ct => await CreateAsync(command.Request, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<ParentProfileDto>> CreateAsync(
        CreateParentRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var fullName = Normalize.Required(request.FullName, string.Empty);
        var email = Normalize.Email(request.Email);

        AccountResolution account;

        if (request.UserId is { } userId)
        {
            account = await accounts.LinkAsync(userId, tenantId, "Parents", cancellationToken).ConfigureAwait(false);

            if (account.Failure is null
                && await parents.Query.AnyAsync(p => p.UserId == userId, cancellationToken).ConfigureAwait(false))
            {
                return Result<ParentProfileDto>.Failure(
                    "Parents.ProfileAlreadyExists",
                    "This user already has a parent profile.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.AccountPassword))
        {
            account = await accounts.CreateAsync(
                    email ?? string.Empty,
                    request.AccountPassword,
                    fullName,
                    request.PhoneNumber,
                    RoleNames.Parent,
                    tenantId,
                    "Parents",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            account = AccountResolution.None;
        }

        if (account.Failure is { } failure)
        {
            return failure.ToResult<ParentProfileDto>();
        }

        var profile = new ParentProfile
        {
            TenantId = tenantId,
            UserId = account.User?.Id,
            FullName = fullName,
            PhoneNumber = Normalize.Optional(request.PhoneNumber),
            Email = email,
            Notes = Normalize.Optional(request.Notes)
        };

        await parents.AddAsync(profile, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ParentProfileDto>.Success(
            await readModel.BuildParentAsync(profile, cancellationToken).ConfigureAwait(false),
            "Parent created successfully.");
    }
}

/// <summary>Partially updates a parent's profile. The linked account is not touched.</summary>
public sealed record UpdateParentCommand(Guid Id, UpdateParentRequest Request)
    : IRequest<Result<ParentProfileDto>>;

public sealed class UpdateParentCommandHandler(
    IRepository<ParentProfile> parents,
    StudentReadModel readModel,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateParentCommand, Result<ParentProfileDto>>
{
    public async Task<Result<ParentProfileDto>> Handle(
        UpdateParentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var profile = await parents.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (profile is null)
        {
            return Result<ParentProfileDto>.Failure("Parents.NotFound", "Parent not found.");
        }

        var request = command.Request;

        if (request.FullName is not null)
        {
            profile.FullName = Normalize.Required(request.FullName, profile.FullName);
        }

        if (request.PhoneNumber is not null)
        {
            profile.PhoneNumber = Normalize.Optional(request.PhoneNumber);
        }

        if (request.Email is not null)
        {
            profile.Email = Normalize.Email(request.Email);
        }

        if (request.Notes is not null)
        {
            profile.Notes = Normalize.Optional(request.Notes);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ParentProfileDto>.Success(
            await readModel.BuildParentAsync(profile, cancellationToken).ConfigureAwait(false),
            "Parent updated successfully.");
    }
}

/// <summary>
/// Soft-deletes a parent and hard-removes their guardian links. The linked account (if any)
/// is left to the Users feature.
/// </summary>
public sealed record DeleteParentCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteParentCommandHandler(
    IRepository<ParentProfile> parents,
    IRepository<StudentGuardian> guardians,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteParentCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteParentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var profile = await parents.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (profile is null)
        {
            return Result<bool>.Failure("Parents.NotFound", "Parent not found.");
        }

        var links = await guardians.Query
            .Where(g => g.ParentProfileId == profile.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var link in links)
        {
            guardians.Remove(link);
        }

        parents.SoftRemove(profile);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<bool>.Success(true, "Parent deleted successfully.");
    }
}
