using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Tenancy;

namespace EducationHub.Application.Common.Interfaces;

/// <summary>
/// Everything needed to mint a token pair, loaded in one round trip.
/// </summary>
/// <param name="User">The account.</param>
/// <param name="Roles">Role names assigned to the account.</param>
/// <param name="Permissions">Flattened permission codes from the user's role claims.</param>
/// <param name="TenantName">
/// Display name of the user's tenant; <c>null</c> for platform administrators (no tenant).
/// </param>
public sealed record AuthenticationSnapshot(
    ApplicationUser User,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    string? TenantName = null)
{
    public bool IsPlatformAdmin =>
        Roles.Contains(Shared.Common.Constants.RoleNames.PlatformAdmin, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The single, explicitly narrow way to read Identity and refresh-token data with the
/// global tenant filter bypassed.
/// </summary>
/// <remarks>
/// <para>
/// This is a deliberate hole in the isolation model and it exists for one reason:
/// <b>authentication happens before tenant context is known</b>. At login time no JWT
/// exists yet, so there is no tenant claim to filter on, and the generic
/// <see cref="IRepository{TEntity}"/> would return zero rows for every login. Refresh-token
/// rotation has the same problem - the presented token is what identifies the tenant.
/// </para>
/// <para>
/// It is kept small and named for one job so that every bypass is greppable. Nothing else
/// in the application may use <c>IgnoreQueryFilters</c>: normal reads go through
/// <see cref="IRepository{TEntity}"/>, which stays filtered and therefore fail-closed.
/// </para>
/// <para>
/// Implementations must additionally verify that a non-platform user is not being
/// authenticated into a tenant that is not active, since a filter cannot express that.
/// </para>
/// </remarks>
public interface IAuthenticationRepository
{
    /// <summary>Loads the user, their roles and their permissions by normalized email.</summary>
    Task<AuthenticationSnapshot?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the snapshot of the one account whose mobile number matches one of
    /// <paramref name="phoneVariants"/> (the account's own number, or the number on the parent
    /// profile linked to it). Returns null when no account - or more than one - matches, so an
    /// ambiguous number can never sign into the wrong account.
    /// </summary>
    Task<AuthenticationSnapshot?> GetByPhoneAsync(IReadOnlyList<string> phoneVariants, CancellationToken cancellationToken = default);

    /// <summary>Loads the same snapshot by user id. Used by the refresh flow.</summary>
    Task<AuthenticationSnapshot?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a tenant without the tenant query filter, because at login time the filter
    /// cannot be applied (the tenant is not known until the user row is read) yet the
    /// tenant must still be checked for active status and subscription expiry.
    /// </summary>
    Task<Tenant?> GetTenantForAuthenticationAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a refresh token by its stored hash, ignoring filters so the owning tenant can
    /// be discovered from the token itself.
    /// </summary>
    Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every token descended from one login. Used to revoke a whole chain when reuse is
    /// detected.
    /// </summary>
    Task<IReadOnlyList<RefreshToken>> GetTokenFamilyAsync(Guid familyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically marks a still-live token as rotated to <paramref name="replacedByTokenId"/>, in
    /// the database, at once. Returns false when another request revoked or rotated it first, so
    /// of several concurrent refreshes with one token exactly one can succeed.
    /// </summary>
    Task<bool> TryClaimRefreshTokenAsync(
        Guid tokenId,
        Guid replacedByTokenId,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes a single token and optionally links it to its replacement.</summary>
    Task RevokeRefreshTokenAsync(
        RefreshToken token,
        string reason,
        Guid? replacedByTokenId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes an entire family in one pass. Called when an already-rotated token is
    /// replayed, which is the signature of a stolen token.
    /// </summary>
    Task RevokeTokenFamilyAsync(
        Guid familyId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every live refresh token for a user <em>without saving</em>. Used by the
    /// password-change flow, where the caller commits the mutation and the revocations in a
    /// single unit of work, so a crash mid-save can never leave a pre-change session alive.
    /// </summary>
    Task RevokeAllForUserAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken = default);
}
