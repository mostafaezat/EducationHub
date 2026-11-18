using EducationHub.Application.Common.Exceptions;
using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Auth.DTOs;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace EducationHub.Application.Features.Auth.Commands;

/// <summary>
/// Authenticates an e-mail (or mobile number) and password pair and issues a token pair.
/// </summary>
public sealed record LoginCommand(LoginRequest Request) : IRequest<Result<AuthResponse>>;

public sealed class LoginCommandHandler(
    IAuthenticationRepository authentication,
    IPasswordHasher<ApplicationUser> passwordHasher,
    ITokenService tokenService,
    IRepository<RefreshToken> refreshTokens,
    IRepository<LoginHistory> loginHistory,
    IDateTimeService dateTime,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
    : IRequestHandler<LoginCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The identifier is an e-mail address or a mobile number (parents often have no e-mail).
        var identifier = command.Request.Email.Trim();

        var snapshot = PhoneNumberFormat.LooksLikePhone(identifier)
            ? await authentication
                .GetByPhoneAsync(PhoneNumberFormat.LookupVariants(identifier), cancellationToken)
                .ConfigureAwait(false)
            : await authentication
                .GetByEmailAsync(identifier.ToUpperInvariant(), cancellationToken)
                .ConfigureAwait(false);

        if (snapshot is null)
        {
            // Identical response to a wrong password: revealing which emails exist would
            // turn the login form into an account-enumeration oracle.
            throw BusinessRuleException.InvalidCredentials();
        }

        var user = snapshot.User;

        // Password verification uses ASP.NET Core Identity's own hasher and its
        // rehash-on-success logic. Nothing here touches the hash format.
        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash ?? string.Empty, command.Request.Password);

        if (verification is PasswordVerificationResult.Failed)
        {
            // Throttling lives here because there is no tenant context at login time:
            // UserManager-based counting would be filtered into nothing. The fields are
            // Identity's own, so SignInManager semantics still apply if one is introduced.
            user.AccessFailedCount++;

            if (user.AccessFailedCount >= AuthConstants.FailedLoginLockoutThreshold)
            {
                user.LockedOutUntil = dateTime.UtcNow.Add(AuthConstants.FailedLoginLockoutDuration);
            }

            await RecordAttemptAsync(user, succeeded: false, reason: "InvalidPassword", cancellationToken)
                .ConfigureAwait(false);

            // Persist the failed attempt and the counters before the request ends, or the
            // lockout logic would be computed against numbers that are never written.
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            throw BusinessRuleException.InvalidCredentials();
        }

        if (verification is PasswordVerificationResult.SuccessRehashNeeded)
        {
            // The stored hash uses outdated parameters (cost/algorithm upgrade). Rewrite it
            // transparently instead of forcing a password reset.
            user.PasswordHash = passwordHasher.HashPassword(user, command.Request.Password);
        }

        await EnsureUserCanAuthenticateAsync(user, cancellationToken).ConfigureAwait(false);

        user.LastLoginAt = dateTime.UtcNow;
        user.LastLoginIp = currentUser.IpAddress;

        await RecordAttemptAsync(user, succeeded: true, reason: null, cancellationToken)
            .ConfigureAwait(false);

        // A successful sign-in clears the failure counters, so one good password never
        // leaves a stale lockout armed against the owner.
        user.AccessFailedCount = 0;
        user.LockedOutUntil = null;

        var result = await IssueTokensAsync(snapshot, command.Request.RememberMe, cancellationToken)
            .ConfigureAwait(false);

        // The rehash (if any), the LastLogin fields, the login-history row and the refresh
        // token are all committed together. Before this call, login recorded nothing at all.
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }

    private async Task EnsureUserCanAuthenticateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (!user.IsActive)
        {
            throw BusinessRuleException.AccountInactive();
        }

        if (user.IsLockedOut)
        {
            throw new BusinessRuleException("Auth.AccountLocked", "This account is temporarily locked.");
        }

        // P0-10: an account is not usable until its email is confirmed. Blocking here (rather
        // than relying on the SignIn.RequireConfirmedEmail option, which Identity's lockout
        // manager never consults on this direct-hash path) is what actually enforces the rule.
        if (!user.EmailConfirmed)
        {
            throw BusinessRuleException.EmailNotConfirmed();
        }

        // A platform administrator belongs to no tenant, so there is no tenant to check.
        if (user.TenantId == Guid.Empty)
        {
            return;
        }

        var tenant = await authentication
            .GetTenantForAuthenticationAsync(user.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null || tenant.IsDeleted)
        {
            throw BusinessRuleException.TenantInactive();
        }

        if (!tenant.CanAccess(dateTime.UtcNow))
        {
            throw tenant.IsSubscriptionExpired(dateTime.UtcNow)
                ? BusinessRuleException.SubscriptionExpired()
                : BusinessRuleException.TenantInactive();
        }
    }

    private async Task<Result<AuthResponse>> IssueTokensAsync(
        AuthenticationSnapshot snapshot,
        bool rememberMe,
        CancellationToken cancellationToken)
    {
        var accessToken = tokenService.CreateAccessToken(snapshot.User, snapshot.Roles, snapshot.Permissions);

        var material = tokenService.CreateRefreshToken();

        var lifetime = rememberMe
            ? AuthConstants.RefreshTokenLifetime
            : AuthConstants.SessionRefreshTokenLifetime;

        var familyId = Guid.NewGuid();

        var refreshToken = new RefreshToken
        {
            UserId = snapshot.User.Id,
            TenantId = snapshot.User.TenantId,
            TokenHash = material.TokenHash,
            FamilyId = familyId,
            
            ExpiresAt = dateTime.UtcNow.Add(lifetime),
            CreatedByIp = currentUser.IpAddress
        };

        await refreshTokens.AddAsync(refreshToken, cancellationToken).ConfigureAwait(false);

        return Result<AuthResponse>.Success(
            new AuthResponse(
                accessToken.Token,
                accessToken.ExpiresAtUtc,
                material.RawToken,
                refreshToken.ExpiresAt,
                AuthConstants.BearerScheme,
                MapProfile(snapshot)),
            "Signed in successfully.");
    }

    private async Task RecordAttemptAsync(
        ApplicationUser user,
        bool succeeded,
        string? reason,
        CancellationToken cancellationToken)
    {
        var entry = new LoginHistory
        {
            UserId = user.Id,
            TenantId = user.TenantId,
            UserName = user.UserName,
            Succeeded = succeeded,
            FailureReason = succeeded ? null : reason,
            IpAddress = currentUser.IpAddress,
            AttemptedAt = dateTime.UtcNow
        };

        await loginHistory.AddAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    internal static UserProfileDto MapProfile(AuthenticationSnapshot snapshot)
    {
        var user = snapshot.User;

        return new UserProfileDto(
            user.Id,
            user.FullName,
            user.Email ?? string.Empty,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.TenantId,
            user.BranchId,
            snapshot.TenantName,
            user.PreferredLanguage,
            user.IsActive,
            user.MustChangePassword,
            snapshot.Roles,
            snapshot.Permissions);
    }
}
