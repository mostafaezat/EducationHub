using System.Security.Cryptography;
using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Users.Commands;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Shared.Common.Localization;
using EducationHub.Shared.Common.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Students.Services;

/// <summary>
/// Outcome of resolving the optional sign-in account for a student or parent profile.
/// </summary>
/// <param name="User">The account to attach, or null when the profile has no account.</param>
/// <param name="Failure">Set when the account could not be linked or created.</param>
public readonly record struct AccountResolution(ApplicationUser? User, AccountFailure? Failure)
{
    public static AccountResolution None => new(null, null);

    public static AccountResolution Of(ApplicationUser user) => new(user, null);

    public static AccountResolution Failed(string code, string message)
        => new(null, new AccountFailure(code, message, null));

    public static AccountResolution Failed(IReadOnlyList<Error> errors, string message)
        => new(null, new AccountFailure(null, message, errors));
}

public sealed record AccountFailure(string? Code, string Message, IReadOnlyList<Error>? Errors)
{
    public Result<T> ToResult<T>()
        => Errors is not null
            ? Result<T>.Failure(Errors, Message)
            : Result<T>.Failure(Code ?? "Errors.Unknown", Message);
}

/// <summary>
/// Links an existing account to, or creates a new account for, a student or parent profile -
/// the same create-or-link contract the teachers module uses, with a third "no account" mode.
/// </summary>
/// <remarks>
/// Must run inside the caller's transaction: account creation and the profile insert either
/// both commit or neither does.
/// </remarks>
public sealed class ProfileAccountSupport(
    UserManager<ApplicationUser> userManager,
    IRepository<ApplicationRole> roles)
{
    /// <summary>Loads an existing account of this tenant to link.</summary>
    /// <param name="errorPrefix">Error-code area, e.g. <c>Students</c> or <c>Parents</c>.</param>
    public async Task<AccountResolution> LinkAsync(
        Guid userId,
        Guid tenantId,
        string errorPrefix,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var linked = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);

        // ApplicationUser carries the tenant filter, so a miss already means "not found for
        // you"; the explicit check covers the platform-scope edge.
        return linked is null || linked.TenantId != tenantId
            ? AccountResolution.Failed($"{errorPrefix}.UserNotFound", "The user to link was not found.")
            : AccountResolution.Of(linked);
    }

    /// <summary>Creates a new account carrying <paramref name="roleName"/>.</summary>
    public Task<AccountResolution> CreateAsync(
        string email,
        string password,
        string fullName,
        string? phoneNumber,
        string roleName,
        Guid tenantId,
        string errorPrefix,
        CancellationToken cancellationToken)
        => CreateCoreAsync(email, password, fullName, phoneNumber, roleName, tenantId, errorPrefix, mustChangePassword: false, cancellationToken);

    /// <summary>
    /// Creates a new account with a generated temporary password that must be changed at the
    /// first sign-in. Returns the password so the staff member can hand it over once.
    /// </summary>
    public async Task<(AccountResolution Account, string? TemporaryPassword)> CreateWithTemporaryPasswordAsync(
        string email,
        string fullName,
        string? phoneNumber,
        string roleName,
        Guid tenantId,
        string errorPrefix,
        CancellationToken cancellationToken)
    {
        var password = GenerateTemporaryPassword();

        var account = await CreateCoreAsync(
                email, password, fullName, phoneNumber, roleName, tenantId, errorPrefix, mustChangePassword: true, cancellationToken)
            .ConfigureAwait(false);

        return (account, account.User is null ? null : password);
    }

    /// <summary>
    /// 10 characters from an unambiguous alphabet, always with a digit and a lowercase letter
    /// (the Identity password policy), drawn from a cryptographic random source.
    /// </summary>
    internal static string GenerateTemporaryPassword()
    {
        const string letters = "abcdefghjkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string all = letters + digits;

        var chars = new char[10];

        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }

        chars[RandomNumberGenerator.GetInt32(0, 5)] = letters[RandomNumberGenerator.GetInt32(letters.Length)];
        chars[RandomNumberGenerator.GetInt32(5, 10)] = digits[RandomNumberGenerator.GetInt32(digits.Length)];

        return new string(chars);
    }

    private async Task<AccountResolution> CreateCoreAsync(
        string email,
        string password,
        string fullName,
        string? phoneNumber,
        string roleName,
        Guid tenantId,
        string errorPrefix,
        bool mustChangePassword,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = Normalize.Required(email, string.Empty);

        var existing = await userManager.FindByEmailAsync(normalizedEmail).ConfigureAwait(false);

        if (existing is not null)
        {
            // E-mail is globally unique; the same answer for a clash inside or outside this
            // tenant avoids confirming which accounts exist elsewhere.
            return AccountResolution.Failed(
                "Users.EmailAlreadyExists",
                "A user with this email address already exists.");
        }

        // A mobile number is a sign-in identifier too, so it must point at one account only.
        var canonicalPhone = PhoneNumberFormat.ToCanonical(phoneNumber);

        if (canonicalPhone is not null)
        {
            var variants = PhoneNumberFormat.LookupVariants(canonicalPhone).ToArray();

            var phoneTaken = await userManager.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => u.PhoneNumber != null && variants.Contains(u.PhoneNumber), cancellationToken)
                .ConfigureAwait(false);

            if (phoneTaken)
            {
                return AccountResolution.Failed(
                    "Users.PhoneAlreadyExists",
                    "A user with this mobile number already exists.");
            }
        }

        var roleExists = await roles.Query
            .AnyAsync(r => r.TenantId == tenantId && r.Name == roleName, cancellationToken)
            .ConfigureAwait(false);

        if (!roleExists)
        {
            return AccountResolution.Failed(
                $"{errorPrefix}.RoleMissing",
                $"The {roleName} role does not exist for this tenant.");
        }

        var user = new ApplicationUser
        {
            TenantId = tenantId,
            FullName = Normalize.Required(fullName, string.Empty),
            Email = normalizedEmail,
            UserName = normalizedEmail,
            PhoneNumber = canonicalPhone ?? Normalize.Optional(phoneNumber),
            PreferredLanguage = SupportedLanguages.Default,
            IsActive = true,
            EmailConfirmed = true,
            MustChangePassword = mustChangePassword
        };

        var created = await userManager.CreateAsync(user, password).ConfigureAwait(false);

        if (!created.Succeeded)
        {
            return AccountResolution.Failed(
                UserCommandSupport.IdentityErrorsToErrors(created),
                "The account could not be created.");
        }

        var assigned = await userManager.AddToRoleAsync(user, roleName).ConfigureAwait(false);

        return assigned.Succeeded
            ? AccountResolution.Of(user)
            : AccountResolution.Failed(
                UserCommandSupport.IdentityErrorsToErrors(assigned),
                $"The account was created but the {roleName} role could not be assigned.");
    }
}
