namespace EducationHub.Shared.Common.Models;

/// <summary>
/// A group of permission codes, keyed by area.
/// </summary>
/// <remarks>
/// Shared rather than declared per-feature. Two slices need this shape - the role editor
/// wants the whole catalog, and <c>GET /api/auth/permissions</c> wants the caller's effective
/// set - and they must serialize identically, so a single definition is the only way to
/// guarantee that.
/// </remarks>
/// <param name="Area">Group heading, e.g. <c>Users</c>.</param>
/// <param name="Permissions">Permission codes belonging to the area.</param>
public sealed record PermissionAreaDto(string Area, IReadOnlyList<string> Permissions);