using EducationHub.Application.Common.Interfaces;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Shared.Common.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Users.Commands;

/// <summary>
/// Shared user-command helpers: role-name resolution and Identity error mapping.
/// </summary>
/// <remarks>
/// <para>
/// These live here rather than being methods on <see cref="CreateUserCommandHandler"/> because
/// handlers are registered only as <c>IRequestHandler&lt;,&gt;</c>, never as their concrete
/// types. Injecting one handler into another therefore fails at startup with a DI error that
/// points at the dependency rather than the mistake. A support type takes the same services
/// and is resolvable by the container as an ordinary class.
/// </para>
/// <para>
/// A single copy also keeps the two call sites honest: role resolution must look identical
/// when a user is created and when their roles are replaced, or a role accepted on one path
/// would be rejected on the other.
/// </para>
/// </remarks>
public sealed class UserCommandSupport(IRepository<ApplicationRole> roles, ITenantContext tenantContext)
{
    /// <summary>
    /// Maps requested role names onto roles that actually exist in this tenant.
    /// </summary>
    /// <returns>The valid role names as stored, or <c>null</c> if any requested role was unknown.</returns>
    public async Task<IReadOnlyList<string>?> ResolveRolesAsync(
        IReadOnlyList<string>? requested,
        CancellationToken cancellationToken)
    {
        if (requested is null || requested.Count == 0)
        {
            return [];
        }

        var names = requested
            .Where(static r => !string.IsNullOrWhiteSpace(r))
            .Select(static r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (names.Length == 0)
        {
            return [];
        }

        var roleQuery = roles.Query.Where(r => names.Contains(r.Name));

        // Platform scope disables the global tenant filter, which would otherwise match
        // every tenant's equally named roles. A platform-scope user carries Guid.Empty,
        // so only platform roles are assignable to them; tenant scope needs no extra
        // predicate because the filter already restricts rows to the caller's tenant.
        if (tenantContext.IsPlatformScope)
        {
            roleQuery = roleQuery.Where(r => r.TenantId == Guid.Empty);
        }

        var known = (await roleQuery
            .Select(r => r.Name!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!)
            .ToArray();

        // Case-insensitive comparison, so a request for "teacher" matches a stored "Teacher".
        var knownSet = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);

        if (!names.All(knownSet.Contains))
        {
            return null;
        }

        // Return the names as they are STORED, not as they were requested: AddToRolesAsync
        // compares against the stored value, so echoing back the request casing would fail.
        return [.. names.Select(n => known.First(k => string.Equals(k, n, StringComparison.OrdinalIgnoreCase)))];
    }

    public static IReadOnlyList<Error> IdentityErrorsToErrors(IdentityResult result)
        => [.. result.Errors.Select(e => new Error(e.Code, e.Description))];
}