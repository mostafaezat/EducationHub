namespace EducationHub.Application.Common.Messaging;

/// <summary>Configuration section <c>WhatsApp</c>.</summary>
public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    /// <summary><c>None</c> (default), <c>Log</c> (development: write messages to the log) or <c>Meta</c> (WhatsApp Cloud API).</summary>
    public string Provider { get; set; } = "None";

    /// <summary>Send a new parent account's sign-in details by WhatsApp.</summary>
    public bool SendGuardianCredentials { get; set; } = true;

    /// <summary>Send parents new homework and exam results by WhatsApp.</summary>
    public bool NotifyParents { get; set; } = true;

    /// <summary>Send a guardian a confirmation when their pre-registration request is received.</summary>
    public bool SendRegistrationConfirmation { get; set; }

    /// <summary>Sign-in page included in the credentials message.</summary>
    public string? LoginUrl { get; set; }

    public MetaWhatsAppOptions Meta { get; set; } = new();
}

/// <summary>WhatsApp Cloud API (Meta) settings. The access token belongs in user secrets / environment, never in appsettings.</summary>
public sealed class MetaWhatsAppOptions
{
    public string ApiVersion { get; set; } = "v21.0";

    /// <summary>The business phone number id from the Meta developer console.</summary>
    public string? PhoneNumberId { get; set; }

    public string? AccessToken { get; set; }

    /// <summary>Language code of the approved templates, e.g. <c>ar</c>.</summary>
    public string LanguageCode { get; set; } = "ar";

    /// <summary>
    /// Approved template for <see cref="Interfaces.WhatsAppMessageKind.Credentials"/> with body
    /// parameters {{1}} name, {{2}} sign-in name, {{3}} temporary password, {{4}} sign-in link.
    /// Without a template the message is sent as free text (accepted only inside a 24-hour
    /// conversation window or for test numbers).
    /// </summary>
    public string? CredentialsTemplate { get; set; }

    /// <summary>Approved template for notifications with body parameters {{1}} title, {{2}} details.</summary>
    public string? NotificationTemplate { get; set; }

    /// <summary>
    /// Approved <i>authentication</i>-category template for password-reset codes: body {{1}} the
    /// code, plus the copy-code (URL) button Meta adds to authentication templates, which is sent
    /// the same code. Free text is not an option here in production: a reset is always
    /// business-initiated, outside any 24-hour conversation window.
    /// </summary>
    public string? PasswordResetTemplate { get; set; }

    /// <summary>
    /// Approved template confirming a pre-registration request: {{1}} guardian name, {{2}} student
    /// name, {{3}} centre name.
    /// </summary>
    public string? RegistrationReceivedTemplate { get; set; }
}
