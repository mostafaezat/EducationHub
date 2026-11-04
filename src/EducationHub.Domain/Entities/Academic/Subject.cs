using EducationHub.Domain.Common;

namespace EducationHub.Domain.Entities.Academic;

/// <summary>
/// A tenant-scoped subject. Never a globally hard-coded subject ID (spec section 16):
/// the IDs differ per tenant, so a global reference table would leak and would not be
/// editable per tenant.
/// </summary>
public class Subject : TenantOwnedEntity
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? DescriptionAr { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }
}
