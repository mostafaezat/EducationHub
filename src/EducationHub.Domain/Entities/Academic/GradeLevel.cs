using EducationHub.Domain.Common;

namespace EducationHub.Domain.Entities.Academic;

/// <summary>
/// A configurable educational level such as KG1, KG2 or Grade 3. Seeded per tenant but
/// user-editable, because the platform must not hard-code the KG1..Grade 6 range into
/// business logic (spec section 21).
/// </summary>
public class GradeLevel : TenantOwnedEntity
{
    /// <summary>Short code used in lists and reports, e.g. <c>G3</c>.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? NameAr { get; set; }

    /// <summary>Sort order; KG1 first, then Grade 6.</summary>
    public int Order { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Description { get; set; }

    public string? DescriptionAr { get; set; }
}
