namespace EducationHub.Application.Common.Interfaces;

/// <summary>
/// Per-request diagnostic context: the correlation id that stitches logs together, and the
/// caller's IP.
/// </summary>
/// <remarks>
/// Implemented in the API layer as a scoped service backed by an
/// <see cref="HttpContext"/> accessor, so no Application code ever touches
/// <c>Microsoft.AspNetCore.Http</c>.
/// </remarks>
public interface IDiagnosticContext
{
    /// <summary>Stable id for this request, echoed in responses and attached to audit rows.</summary>
    string CorrelationId { get; }

    string? IpAddress { get; }
}
