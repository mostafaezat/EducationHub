namespace EducationHub.Application.Common.Interfaces;

/// <summary>
/// An outbound email ready for a transport.
/// </summary>
/// <param name="To">Recipient email address.</param>
/// <param name="Subject">Plain subject line.</param>
/// <param name="PlainTextBody">Plain-text body. The current sender is log-only, so plain text
/// is what ends up in the log; an SMTP sender would prefer this too.</param>
/// <param name="HtmlBody">Optional rich body for senders that can render HTML.</param>
public sealed record EmailMessage(
    string To,
    string Subject,
    string PlainTextBody,
    string? HtmlBody = null);