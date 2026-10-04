using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace DayGrid.Infrastructure.Email;

/// <summary>SMTP settings, bound from the "Email" configuration section (see appsettings.json).</summary>
public class EmailSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromName { get; set; } = "DayGrid";
}

/// <summary>Thrown when SMTP settings are missing or still placeholders, so callers can log
/// and move on instead of retrying a send that can never succeed.</summary>
public sealed class EmailNotConfiguredException : InvalidOperationException
{
    public EmailNotConfiguredException(string message) : base(message) { }
}

public interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string htmlBody, CancellationToken ct = default);
}

/// <summary>MailKit-backed SMTP sender. Retries are the caller's responsibility (see
/// ReminderDispatcherService) so this class stays a thin, single-attempt wrapper.</summary>
public class MailKitEmailSender : IEmailSender
{
    private readonly EmailSettings _settings;
    private readonly ILogger<MailKitEmailSender> _logger;

    public MailKitEmailSender(IOptions<EmailSettings> settings, ILogger<MailKitEmailSender> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toAddress, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (!IsConfigured(_settings))
        {
            _logger.LogWarning("Email not sent to {To}: Email:Host/User/Password are not configured (placeholder or empty)", toAddress);
            throw new EmailNotConfiguredException("Email is not configured — set Email:Host, Email:User and Email:Password.");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.User));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            var socketOptions = _settings.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(_settings.Host, _settings.Port, socketOptions, ct);
            await client.AuthenticateAsync(_settings.User, _settings.Password, ct);
            await client.SendAsync(message, ct);
        }
        finally
        {
            if (client.IsConnected)
                await client.DisconnectAsync(true, CancellationToken.None);
        }
    }

    private static bool IsConfigured(EmailSettings s) =>
        !string.IsNullOrWhiteSpace(s.Host)
        && !string.IsNullOrWhiteSpace(s.User)
        && !string.IsNullOrWhiteSpace(s.Password)
        && !s.Password.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
        && !(s.Password.StartsWith('<') && s.Password.EndsWith('>'));
}
