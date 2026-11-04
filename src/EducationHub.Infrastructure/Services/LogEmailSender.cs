using EducationHub.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace EducationHub.Infrastructure.Services;

/// <summary>
/// Development-only email sender that writes messages to structured logs instead of delivering
/// them.
/// </summary>
/// <remarks>
/// <para>
/// There is no SMTP infrastructure in Phase 0. This sender makes the full reset/confirmation
/// flows real end-to-end: links are logged and are therefore usable in development, yet nothing
/// is ever transmitted. The token itself appears in the body so the flow can be exercised from
/// the log.
/// </para>
/// <para>
/// This type MUST NOT be registered in production configuration. When SMTP arrives it is
/// replaced by a real implementation of <see cref="IEmailSender"/>; keep the interface stable
/// and swap the registration.
/// </para>
/// </remarks>
public sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        logger.LogInformation(
            "EMAIL to {Recipient} | {Subject}{NewLine}{Body}",
            message.To,
            message.Subject,
            System.Environment.NewLine,
            message.PlainTextBody);

        return Task.CompletedTask;
    }
}