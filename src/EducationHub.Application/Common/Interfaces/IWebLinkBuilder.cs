namespace EducationHub.Application.Common.Interfaces;

/// <summary>
/// Builds the external links embedded in auth emails.
/// </summary>
/// <remarks>
/// Handlers must never accept a base URL from the client: the caller cannot be trusted to name
/// the origin that hosts the confirmation UI, so the link destination is a server-side choice
/// implemented in Infrastructure from configuration.
/// </remarks>
public interface IWebLinkBuilder
{
    /// <summary>Frontend link for finishing a password reset.</summary>
    string PasswordResetLink(string email, string token);

    /// <summary>Frontend link for confirming an email address.</summary>
    string EmailConfirmationLink(string email, string token);
}