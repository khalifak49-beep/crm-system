using CrmSystem.Data;
using CrmSystem.Models.Entities;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace CrmSystem.Services;

public sealed record SmtpOptions(string Host, int Port, bool EnableSsl, string? UserName, string? Password, string FromEmail, string FromName)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromEmail) && Port > 0;
}

/// <summary>
/// Sends email through the SMTP server configured in Settings (MailKit).
/// The password is stored encrypted in SystemSettings and only decrypted here.
/// </summary>
public sealed class EmailSender
{
    private readonly SecretProtector _secrets;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(SecretProtector secrets, ILogger<EmailSender> logger)
    {
        _secrets = secrets;
        _logger = logger;
    }

    public async Task<SmtpOptions> LoadOptionsAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var values = await db.SystemSettings.AsNoTracking()
            .Where(s => s.GroupName == "Smtp")
            .ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue, StringComparer.OrdinalIgnoreCase, ct);
        string Get(string key) => values.TryGetValue(key, out var v) ? v ?? "" : "";

        return new SmtpOptions(
            Get(SettingKeys.SmtpHost).Trim(),
            int.TryParse(Get(SettingKeys.SmtpPort), out var port) ? port : 587,
            !bool.TryParse(Get(SettingKeys.SmtpEnableSsl), out var ssl) || ssl,
            string.IsNullOrWhiteSpace(Get(SettingKeys.SmtpUsername)) ? null : Get(SettingKeys.SmtpUsername).Trim(),
            _secrets.Unprotect(Get(SettingKeys.SmtpPassword)),
            Get(SettingKeys.SmtpFromEmail).Trim(),
            string.IsNullOrWhiteSpace(Get(SettingKeys.SmtpFromName)) ? "CRM System" : Get(SettingKeys.SmtpFromName).Trim());
    }

    public async Task SendAsync(SmtpOptions options, string toEmail, string subject, string textBody, string htmlBody, CancellationToken ct)
    {
        if (!options.IsConfigured) throw new InvalidOperationException("SMTP is not configured.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.FromName, options.FromEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { TextBody = textBody, HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 30000 };
        var security = options.Port == 465 ? SecureSocketOptions.SslOnConnect
                     : options.EnableSsl ? SecureSocketOptions.StartTls
                     : SecureSocketOptions.None;
        await client.ConnectAsync(options.Host, options.Port, security, ct);
        if (!string.IsNullOrEmpty(options.UserName))
            await client.AuthenticateAsync(options.UserName, options.Password ?? string.Empty, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
        _logger.LogInformation("Email sent to {To}: {Subject}", toEmail, subject);
    }

    /// <summary>Simple, mail-client-safe HTML (inline styles, RTL aware).</summary>
    public static string BuildHtml(string title, string body, string? linkUrl, string? linkText, bool rtl, string companyName)
    {
        static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);
        var dir = rtl ? "rtl" : "ltr";
        var align = rtl ? "right" : "left";
        var lines = string.Join("<br>", body.Split('\n').Select(Enc));
        var button = string.IsNullOrEmpty(linkUrl) ? "" :
            $"<p style=\"margin:24px 0 0\"><a href=\"{Enc(linkUrl)}\" style=\"background:#007AFF;color:#fff;text-decoration:none;padding:10px 18px;border-radius:10px;display:inline-block;font-weight:600\">{Enc(linkText ?? "Open")}</a></p>";
        return $@"<!DOCTYPE html><html dir=""{dir}""><body style=""margin:0;background:#f2f2f7;font-family:-apple-system,Segoe UI,Tahoma,Arial,sans-serif"">
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""padding:24px 12px""><tr><td align=""center"">
<table role=""presentation"" width=""100%"" style=""max-width:560px;background:#ffffff;border-radius:16px;padding:28px;text-align:{align}"">
<tr><td style=""font-size:13px;color:#8a8a8e;padding-bottom:8px"">{Enc(companyName)}</td></tr>
<tr><td style=""font-size:19px;font-weight:600;color:#1c1c1e;padding-bottom:14px"">{Enc(title)}</td></tr>
<tr><td style=""font-size:15px;line-height:1.6;color:#3c3c43"">{lines}{button}</td></tr>
</table></td></tr></table></body></html>";
    }
}
