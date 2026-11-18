using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Domain.Entities.Students;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EducationHub.Application.Common.Messaging;

/// <summary>A learning update for the parents of one student.</summary>
public sealed record ParentUpdate(Guid StudentProfileId, string Title, string Details);

/// <summary>
/// Builds and sends the product's WhatsApp messages: a new parent's sign-in details and learning
/// updates for parents. Everything here is best-effort - a failed send is logged and never fails
/// the operation that triggered it.
/// </summary>
public sealed class WhatsAppMessenger(
    IWhatsAppSender sender,
    IOptions<WhatsAppOptions> options,
    IRepository<StudentGuardian> guardians,
    IRepository<ParentProfile> parents,
    IRepository<StudentProfile> students,
    ILogger<WhatsAppMessenger> logger)
{
    private readonly WhatsAppOptions _options = options.Value;

    /// <summary>Sends a new parent account's sign-in details; false when not sent.</summary>
    public async Task<bool> SendGuardianCredentialsAsync(
        string? phoneNumber,
        string parentName,
        string loginName,
        string temporaryPassword,
        CancellationToken cancellationToken)
    {
        var phone = PhoneNumberFormat.ToCanonical(phoneNumber);

        if (phone is null || !_options.SendGuardianCredentials || !sender.IsEnabled)
        {
            return false;
        }

        var link = _options.LoginUrl ?? string.Empty;

        var text = string.Join(
            "\n",
            $"أهلاً {parentName}،",
            "تم إنشاء حساب ولي الأمر لمتابعة أبنائك (الحضور والواجبات والامتحانات والمصاريف).",
            $"اسم الدخول: {loginName}",
            $"كلمة المرور المؤقتة: {temporaryPassword}",
            string.IsNullOrWhiteSpace(link) ? string.Empty : $"رابط الدخول: {link}",
            "هيُطلب منك تغيير كلمة المرور أول مرة تدخل.");

        return await TrySendAsync(
                new WhatsAppMessage(phone, WhatsAppMessageKind.Credentials, text, [parentName, loginName, temporaryPassword, link]),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Sends a one-time password-reset code; false when not sent.</summary>
    public async Task<bool> SendPasswordResetCodeAsync(string? phoneNumber, string code, CancellationToken cancellationToken)
    {
        var phone = PhoneNumberFormat.ToCanonical(phoneNumber);

        if (phone is null || !sender.IsEnabled)
        {
            return false;
        }

        var text = string.Join(
            "\n",
            $"رمز إعادة تعيين كلمة المرور: {code}",
            "الرمز صالح لبضع دقائق. لا تشاركه مع أي شخص.",
            "إذا لم تطلب إعادة التعيين فتجاهل هذه الرسالة.");

        return await TrySendAsync(new WhatsAppMessage(phone, WhatsAppMessageKind.PasswordReset, text, [code]), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Confirms to a guardian that their pre-registration request was received; false when not sent.</summary>
    public async Task<bool> SendRegistrationReceivedAsync(
        string? phoneNumber,
        string guardianName,
        string studentName,
        string centreName,
        CancellationToken cancellationToken)
    {
        var phone = PhoneNumberFormat.ToCanonical(phoneNumber);

        if (phone is null || !_options.SendRegistrationConfirmation || !sender.IsEnabled)
        {
            return false;
        }

        var text = string.Join(
            "\n",
            $"أهلاً {guardianName}،",
            $"وصلنا طلب التسجيل المبدئي للطالب/ة {studentName} في {centreName}.",
            "سيتواصل معك فريق السنتر قريباً لتأكيد التسجيل.");

        return await TrySendAsync(
                new WhatsAppMessage(phone, WhatsAppMessageKind.RegistrationReceived, text, [guardianName, studentName, centreName]),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Sends each update to every parent of its student who has a mobile number; returns how many were sent.</summary>
    public async Task<int> NotifyParentsAsync(IReadOnlyList<ParentUpdate> updates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updates);

        if (updates.Count == 0 || !_options.NotifyParents || !sender.IsEnabled)
        {
            return 0;
        }

        var studentIds = updates.Select(static u => u.StudentProfileId).Distinct().ToArray();

        var phones = await guardians.Query
            .Where(g => studentIds.Contains(g.StudentProfileId))
            .Join(
                parents.Query.Where(static p => p.PhoneNumber != null),
                g => g.ParentProfileId,
                p => p.Id,
                (g, p) => new { g.StudentProfileId, p.PhoneNumber })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (phones.Count == 0)
        {
            return 0;
        }

        var names = await students.Query
            .Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(static s => s.Id, static s => s.FullName, cancellationToken)
            .ConfigureAwait(false);

        var sent = 0;
        var done = new HashSet<(string Phone, string Text)>();

        foreach (var update in updates)
        {
            var studentName = names.GetValueOrDefault(update.StudentProfileId, string.Empty);
            var details = string.IsNullOrWhiteSpace(studentName) ? update.Details : $"{studentName}: {update.Details}";
            var text = $"{update.Title}\n{details}";

            foreach (var row in phones.Where(p => p.StudentProfileId == update.StudentProfileId))
            {
                var phone = PhoneNumberFormat.ToCanonical(row.PhoneNumber);

                if (phone is null || !done.Add((phone, text)))
                {
                    continue;
                }

                if (await TrySendAsync(
                        new WhatsAppMessage(phone, WhatsAppMessageKind.Notification, text, [update.Title, details]),
                        cancellationToken)
                    .ConfigureAwait(false))
                {
                    sent++;
                }
            }
        }

        return sent;
    }

    private async Task<bool> TrySendAsync(WhatsAppMessage message, CancellationToken cancellationToken)
    {
        try
        {
            return await sender.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Best-effort by contract: a provider bug must not fail the business operation.
            logger.LogWarning(ex, "WhatsApp message to {Phone} could not be sent.", message.ToPhone);
            return false;
        }
    }
}
