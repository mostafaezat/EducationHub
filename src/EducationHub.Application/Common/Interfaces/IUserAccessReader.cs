namespace EducationHub.Application.Common.Interfaces;

/// <summary>
/// Batch reads of the Identity role/permission join tables.
/// </summary>
/// <remarks>
/// <para>
/// These live behind an interface for two reasons. First, the join tables
/// (<c>UserRoles</c>, <c>RoleClaims</c>) are Identity internals with no tenant filter of
/// their own, so every access to them belongs in Infrastructure where the schema knowledge
/// lives. Second, the alternative - calling <c>UserManager.GetRolesAsync</c> per user -
/// turns a 20-row page into 21 round trips.
/// </para>
/// <para>
/// Callers still pass user ids that came from a tenant-filtered query, so a tenant
/// administrator can only ever resolve roles for users of their own tenant.
/// </para>
/// </remarks>
public interface IUserAccessReader
{
    /// <summary>Role names per user id.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetRolesForUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>Flattened permission codes per user id, resolved through role claims.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetPermissionsForUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// User ids holding a given role, resolved from the Identity join table.
    /// </summary>
    /// <remarks>
    /// The result is deliberately <b>unfiltered by tenant</b>: it is a set of ids intended
    /// to be combined with an already tenant-filtered user query, e.g.
    /// <c>userManager.Users.Where(u =&gt; ids.Contains(u.Id))</c>. Combining them in that
    /// order keeps the tenant filter in charge and makes the result translatable to SQL -
    /// unlike filtering by role after paging, which silently returns wrong totals.
    /// </remarks>
    Task<IReadOnlyList<Guid>> GetUserIdsByRoleAsync(
        string roleName,
        CancellationToken cancellationToken = default);

    /// <summary>Permission codes per role id, read from the Identity role-claims table.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetPermissionsByRoleIdsAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Member count per role id.
    /// </summary>
    /// <remarks>
    /// Grouped in a single query rather than calling <c>GetUsersInRoleAsync</c> per role,
    /// which would make the role list endpoint issue one extra round trip per row. Roles
    /// with no members are absent from the result, so callers read it with
    /// <c>GetValueOrDefault(id)</c> and treat a miss as zero.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, int>> GetUserCountsByRoleIdsAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default);
}
