using EducationHub.Application.Common.Exceptions;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Auth.Commands;
using EducationHub.Application.Features.Auth.DTOs;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;

namespace EducationHub.Application.Features.Auth.Queries;

/// <summary>
/// Returns the caller's own profile.
/// </summary>
/// <remarks>
/// The SPA calls this on every page load to rebuild its state from a stored refresh token.
/// It must therefore work from the access token alone - it takes no tenant input, because
/// the tenant was fixed when the token was issued.
/// </remarks>
public sealed record GetCurrentUserQuery : IRequest<Result<UserProfileDto>>;

public sealed class GetCurrentUserQueryHandler(
    IAuthenticationRepository authentication,
    ICurrentUser currentUser)
    : IRequestHandler<GetCurrentUserQuery, Result<UserProfileDto>>
{
    public async Task<Result<UserProfileDto>> Handle(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new BusinessRuleException("Auth.Unauthenticated", "Authentication is required.");

        var snapshot = await authentication.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);

        if (snapshot is null)
        {
            throw new BusinessRuleException("Account.NotFound", "The account no longer exists.");
        }

        return Result<UserProfileDto>.Success(LoginCommandHandler.MapProfile(snapshot));
    }
}

/// <summary>Lists the permission catalog so the frontend can render the role editor.</summary>
public sealed record GetPermissionsQuery : IRequest<Result<IReadOnlyList<PermissionAreaDto>>>;

public sealed class GetPermissionsQueryHandler : IRequestHandler<GetPermissionsQuery, Result<IReadOnlyList<PermissionAreaDto>>>
{
    public Task<Result<IReadOnlyList<PermissionAreaDto>>> Handle(
        GetPermissionsQuery request,
        CancellationToken cancellationToken)
    {
        // Served from the shared catalog rather than the database, so the frontend can never
        // offer a permission the server does not recognize.
        var areas = PermissionConstants.Areas
            .Select(static area => new PermissionAreaDto(area.Key, area.Value))
            .ToArray();

        return Task.FromResult(Result<IReadOnlyList<PermissionAreaDto>>.Success(areas));
    }
}
