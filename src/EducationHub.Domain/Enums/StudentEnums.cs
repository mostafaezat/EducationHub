namespace EducationHub.Domain.Enums;

/// <summary>Where a student stands with the school. Stored as an int.</summary>
public enum StudentStatus
{
    /// <summary>Currently studying.</summary>
    Active = 0,

    /// <summary>Temporarily not attending (kept on the books).</summary>
    Inactive = 1,

    /// <summary>Finished their final grade.</summary>
    Graduated = 2,

    /// <summary>Left the school.</summary>
    Withdrawn = 3
}

/// <summary>How a parent/guardian is related to a student. Stored as an int.</summary>
public enum GuardianRelationship
{
    Father = 0,

    Mother = 1,

    /// <summary>Legal guardian who is not a parent.</summary>
    Guardian = 2,

    Other = 3
}

public enum RegistrationRequestStatus
{
    Pending = 0,

    Approved = 1,

    Rejected = 2
}
