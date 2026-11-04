using EducationHub.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace EducationHub.Infrastructure.Services;

/// <summary>
/// Builds auth email links from server configuration (<c>Auth:Email:FrontendBaseUrl</c>), never
/// from the caller.
/// </summary>
public sealed class WebLinkBuilder(IConfiguration configuration) : IWebLinkBuilder
{
    private string FrontendBaseUrl =>
        configuration["Auth:Email:FrontendBaseUrl"] ?? "http://localhost:4200";

    public string PasswordResetLink(string email, string token)
        => Compose("reset-password", email, token);

    public string EmailConfirmationLink(string email, string token)
        => Compose("confirm-email", email, token);

    private string Compose(string page, string email, string token)
        => $"{FrontendBaseUrl}/auth/{page}?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
}