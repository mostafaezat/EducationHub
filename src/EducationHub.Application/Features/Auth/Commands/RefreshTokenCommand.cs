using EducationHub.Application.Common.Exceptions;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Auth.DTOs;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Shared.Common.Models;
using MediatR;

namespace EducationHub.Application.Features.Auth.Commands;

/// <summary>
/// Exchanges a refresh token for a new token pair, rotating the presented token.
/// </summary>
public sealed record RefreshTokenCommand(RefreshTokenRequest Request) : IRequest<Result<AuthResponse>>;

/// <summary>
/// Implements refresh-token rotation with reuse detection.
/// </summary>
/// <remarks>
/// <para>
/// Every successful refresh revokes the presented token and issues a replacement linked to
/// it. That alone would not stop an attacker: if a token is stolen, the attacker and the
/// legitimate user can both refresh, and each use silently invalidates the other.
/// </para>
/// <para>
/// So a presented token that has <b>already</b> been rotated is treated as stolen. The
/// entire <see cref="RefreshToken.FamilyId"/> chain is revoked, which logs out the attacker
/// and the victim together - and forces the victim to re-authenticate, which is the correct
/// outcome for a compromised session.
/// </para>
/// </remarks>
public sealed class RefreshTokenCommandHandler(
    IAuthenticationRepository authentication,
    ITokenService tokenService,
    IRepository<RefreshToken> refreshTokens,
    ICurrentUser currentUser,
    IDateTimeService dateTime)
    : IRequestHandler<RefreshTokenCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var raw = command.Request.RefreshToken;

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new BusinessRuleException("Auth.InvalidRefreshToken", "A refresh token is required.");
        }

        var presentedHash = tokenService.HashRefreshToken(raw);

        var stored = await authentication
            .FindRefreshTokenAsync(presentedHash, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            // Unknown hash: the token was forged, already purged, or belongs to no session.
            throw new BusinessRuleException("Auth.InvalidRefreshToken", "The refresh token is not valid.");
        }

        // Reuse of an already-rotated token. Checked BEFORE the expiry test on purpose: an
        // expired-but-rotated token replayed later is still evidence of theft, not a
        // harmless mistake.
        if (stored.RevokedAt is not null && stored.ReplacedByTokenId is not null)
        {
            await authentication
                .RevokeTokenFamilyAsync(stored.FamilyId, "ReuseDetected", cancellationToken)
                .ConfigureAwait(false);

            throw BusinessRuleException.ReuseDetected();
        }

        if (stored.RevokedAt is not null)
        {
            throw new BusinessRuleException(
                "Auth.InvalidRefreshToken",
                "This session has been signed out.");
        }

        if (!stored.IsActive(dateTime.UtcNow))
        {
            throw new BusinessRuleException("Auth.InvalidRefreshToken", "The refresh token has expired.");
        }

        // Defence in depth: the hash lookup already matched, but a cheap constant-time
        // compare means a partially corrupted row cannot be honoured.
        if (!tokenService.VerifyRefreshToken(raw, stored.TokenHash))
        {
            await authentication
                .RevokeTokenFamilyAsync(stored.FamilyId, "HashMismatch", cancellationToken)
                .ConfigureAwait(false);

            throw new BusinessRuleException("Auth.InvalidRefreshToken", "The refresh token is not valid.");
        }

        var snapshot = await authentication
            .GetByIdAsync(stored.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null)
        {
            throw new BusinessRuleException("Auth.InvalidRefreshToken", "The session owner no longer exists.");
        }

        if (!snapshot.User.IsActive || snapshot.User.IsLockedOut)
        {
            await authentication
                .RevokeTokenFamilyAsync(stored.FamilyId, "AccountInactive", cancellationToken)
                .ConfigureAwait(false);

            throw BusinessRuleException.AccountInactive();
        }

        // Unconfirmed accounts cannot hold a session: a refresh token must not keep working
        // for a caller who has never verified their email, so the whole family is revoked.
        if (!snapshot.User.EmailConfirmed)
        {
            await authentication
                .RevokeTokenFamilyAsync(stored.FamilyId, "EmailNotConfirmed", cancellationToken)
                .ConfigureAwait(false);

            throw BusinessRuleException.EmailNotConfirmed();
        }

        var tenant = snapshot.User.TenantId == Guid.Empty
            ? null
            : await authentication
                .GetTenantForAuthenticationAsync(snapshot.User.TenantId, cancellationToken)
                .ConfigureAwait(false);

        if (snapshot.User.TenantId != Guid.Empty && (tenant is null || !tenant.CanAccess(dateTime.UtcNow)))
        {
            await authentication
                .RevokeTokenFamilyAsync(stored.FamilyId, "TenantInactive", cancellationToken)
                .ConfigureAwait(false);

            throw tenant is not null && tenant.IsSubscriptionExpired(dateTime.UtcNow)
                ? BusinessRuleException.SubscriptionExpired()
                : BusinessRuleException.TenantInactive();
        }

        var access = tokenService.CreateAccessToken(snapshot.User, snapshot.Roles, snapshot.Permissions);
        var material = tokenService.CreateRefreshToken();

        // The replacement inherits the family, which is what makes future reuse of this
        // chain detectable and revokable as a unit.
        var replacement = new RefreshToken
        {
            UserId = snapshot.User.Id,
            TenantId = snapshot.User.TenantId,
            TokenHash = material.TokenHash,
            FamilyId = stored.FamilyId,

            // The replacement inherits the ORIGINAL lifetime rather than being renewed, so
            // continuous refresh cannot extend a session past the point its user chose.
            ExpiresAt = dateTime.UtcNow.Add(stored.ExpiresAt - stored.CreatedAt),
            CreatedByIp = currentUser.IpAddress
        };

        await refreshTokens.AddAsync(replacement, cancellationToken).ConfigureAwait(false);

        // Revoking the presented token and linking it to the replacement is what turns the
        // next presentation of it into a detectable reuse.
        await authentication
            .RevokeRefreshTokenAsync(
                stored,
                "Rotated",
                replacedByTokenId: replacement.Id,
                cancellationToken)
            .ConfigureAwait(false);

        return Result<AuthResponse>.Success(
            new AuthResponse(
                access.Token,
                access.ExpiresAtUtc,
                material.RawToken,
                replacement.ExpiresAt,
                AuthConstants.BearerScheme,
                LoginCommandHandler.MapProfile(snapshot)),
            "Token refreshed.");
    }
}
