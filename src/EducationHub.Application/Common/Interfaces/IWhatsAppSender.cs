namespace EducationHub.Application.Common.Interfaces;

/// <summary>What a WhatsApp message is for; a provider may map each kind to an approved template.</summary>
public enum WhatsAppMessageKind
{
    /// <summary>A new parent account: name, sign-in name, temporary password, sign-in link.</summary>
    Credentials = 0,

    /// <summary>A learning update for a parent: title and details (homework, exam result).</summary>
    Notification = 1,

    /// <summary>
    /// A one-time password-reset code. Parameters: the code. Meta requires an approved
    /// <i>authentication</i> template whose copy-code button repeats the code.
    /// </summary>
    PasswordReset = 2,

    /// <summary>A pre-registration request was received: guardian name, student name, centre name.</summary>
    RegistrationReceived = 3
}

/// <summary>
/// One WhatsApp message. <see cref="Text"/> is the full message for providers that send free text;
/// <see cref="Parameters"/> are the same values in template order for providers that need an
/// approved template (WhatsApp Business only allows templates for business-initiated messages).
/// </summary>
public sealed record WhatsAppMessage(
    string ToPhone,
    WhatsAppMessageKind Kind,
    string Text,
    IReadOnlyList<string> Parameters);

/// <summary>
/// Sends WhatsApp messages. Sending is best-effort: callers never fail a business operation
/// because a message could not be delivered.
/// </summary>
public interface IWhatsAppSender
{
    /// <summary>False when WhatsApp is switched off, so callers can skip building messages.</summary>
    bool IsEnabled { get; }

    /// <summary>Sends one message; returns whether the provider accepted it.</summary>
    Task<bool> SendAsync(WhatsAppMessage message, CancellationToken cancellationToken = default);
}
