namespace EducationHub.Application.Common.Interfaces;

/// <summary>
/// Sends an email. Implementations own the transport concern entirely.
/// </summary>
/// <remarks>
/// The only implementation today is <c>LogEmailSender</c>, which writes messages to the logs -
/// a deliberate, development-only choice until SMTP infrastructure exists. Handlers must treat
/// sending as best-effort and must never depend on a message actually being delivered; the
/// verify-email addresses flow through token generation, not through delivery.
/// </remarks>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}