using System.Net.Http.Headers;
using System.Net.Http.Json;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Common.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EducationHub.Infrastructure.Services;

/// <summary>WhatsApp switched off (<c>WhatsApp:Provider = None</c>): nothing is sent.</summary>
public sealed class DisabledWhatsAppSender : IWhatsAppSender
{
    public bool IsEnabled => false;

    public Task<bool> SendAsync(WhatsAppMessage message, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}

/// <summary>
/// Development sender (<c>WhatsApp:Provider = Log</c>): writes the message to the log instead of
/// sending it, like <see cref="LogEmailSender"/>. Messages can contain a temporary password, so
/// this provider must never be configured in production.
/// </summary>
public sealed class LogWhatsAppSender(ILogger<LogWhatsAppSender> logger) : IWhatsAppSender
{
    public bool IsEnabled => true;

    public Task<bool> SendAsync(WhatsAppMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        logger.LogInformation(
            "WHATSAPP ({Kind}) to {Phone}{NewLine}{Text}",
            message.Kind,
            message.ToPhone,
            Environment.NewLine,
            message.Text);

        return Task.FromResult(true);
    }
}

/// <summary>
/// WhatsApp Cloud API sender (<c>WhatsApp:Provider = Meta</c>). Uses the approved template
/// configured for the message kind; without one it falls back to a free-text message.
/// </summary>
public sealed class MetaWhatsAppSender(
    HttpClient http,
    IOptions<WhatsAppOptions> options,
    ILogger<MetaWhatsAppSender> logger) : IWhatsAppSender
{
    private readonly MetaWhatsAppOptions _meta = options.Value.Meta;

    public bool IsEnabled
        => !string.IsNullOrWhiteSpace(_meta.PhoneNumberId) && !string.IsNullOrWhiteSpace(_meta.AccessToken);

    public async Task<bool> SendAsync(WhatsAppMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!IsEnabled)
        {
            logger.LogWarning("WhatsApp is set to Meta but PhoneNumberId / AccessToken are missing; message not sent.");
            return false;
        }

        var to = new string([.. message.ToPhone.Where(char.IsAsciiDigit)]);

        var template = message.Kind switch
        {
            WhatsAppMessageKind.Credentials => _meta.CredentialsTemplate,
            WhatsAppMessageKind.PasswordReset => _meta.PasswordResetTemplate,
            WhatsAppMessageKind.RegistrationReceived => _meta.RegistrationReceivedTemplate,
            _ => _meta.NotificationTemplate
        };

        var bodyParameters = message.Parameters.Select(static p => new { type = "text", text = p }).ToArray();

        // Authentication templates carry a copy-code button that must be given the code again.
        var components = message.Kind == WhatsAppMessageKind.PasswordReset && message.Parameters.Count > 0
            ? new object[]
            {
                new { type = "body", parameters = bodyParameters },
                new
                {
                    type = "button",
                    sub_type = "url",
                    index = "0",
                    parameters = new[] { new { type = "text", text = message.Parameters[0] } }
                }
            }
            : new object[] { new { type = "body", parameters = bodyParameters } };

        object payload = string.IsNullOrWhiteSpace(template)
            ? new
            {
                messaging_product = "whatsapp",
                to,
                type = "text",
                text = new { body = message.Text }
            }
            : new
            {
                messaging_product = "whatsapp",
                to,
                type = "template",
                template = new
                {
                    name = template,
                    language = new { code = _meta.LanguageCode },
                    components
                }
            };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"https://graph.facebook.com/{_meta.ApiVersion}/{_meta.PhoneNumberId}/messages"))
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _meta.AccessToken);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            logger.LogWarning(
                "WhatsApp send to {Phone} failed with {Status}: {Body}",
                to,
                (int)response.StatusCode,
                body.Length > 500 ? body[..500] : body);

            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "WhatsApp send to {Phone} failed.", to);
            return false;
        }
    }
}
