namespace EducationHub.Infrastructure.Services;

/// <summary>
/// Forces <see cref="Application.Common.Interfaces.ITenantContext.IsPlatformScope"/> for the
/// duration of a call.
/// </summary>
/// <remarks>
/// <para>
/// Needed for one situation: work that runs before any request exists. Seeding creates the
/// platform administrator (a tenant-less row) and the per-tenant system roles, both of which
/// require platform scope - and <see cref="HttpTenantContext"/> derives scope from the
/// signed principal, of which there is none at startup. Without this the seeder would be
/// refused by the same save-time check that protects a real request, or would quietly fail
/// to see the roles it was asked to update.
/// </para>
/// <para>
/// Ambient and explicitly scoped rather than a boolean someone can set: <c>Begin</c> returns
/// a disposable, so scope ends with the <c>using</c> and cannot leak into a later request.
/// The value is <c>AsyncLocal</c>, so it does not cross into unrelated work on other threads.
/// </para>
/// <para>
/// Host code holding this flag can already do anything to the database by virtue of running
/// in-process, so elevating scope grants no new capability - it only keeps the seeder from
/// having to bypass the isolation filter it would otherwise be validating.
/// </para>
/// </remarks>
public sealed class PlatformScopeOverride
{
    private static readonly AsyncLocal<bool> Enabled = new();

    /// <summary>True while an override is active on the current async flow.</summary>
    public static bool IsActive => Enabled.Value;

    /// <summary>Starts the override. Dispose the result to end it.</summary>
    public static IDisposable Begin()
    {
        var previous = Enabled.Value;

        Enabled.Value = true;

        return new Revert(previous);
    }

    private sealed class Revert(bool previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Enabled.Value = previous;
        }
    }
}