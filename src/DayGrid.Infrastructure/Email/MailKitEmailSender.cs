using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace DayGrid.Infrastructure.Email;

/// <summary>How outgoing email is delivered (config <c>Email:Mode</c>).</summary>
public enum EmailDeliveryMode
{
    /// <summary>Send through the SMTP server in Email:Host (default).</summary>
    Smtp,

    /// <summary>Write each message as an .eml file into Email:PickupDirectory (local dev, e2e tests).</summary>
    Pickup
}

/// <summary>Email settings, bound from the "Email" configuration section (see appsettings.json).</summary>
public class EmailSettings
{
    public EmailDeliveryMode Mode { get; set; } = EmailDeliveryMode.Smtp;

    /// <summary>Pickup mode: folder the .eml files are written to (created if missing). Relative
    /// paths resolve against the application's base directory.</summary>
    public string PickupDirectory { get; set; } = string.Empty;

    /// <summary>Sender address; defaults to <see cref="User"/> (or no-reply@daygrid.local when that is empty too).</summary>
    public string FromAddress { get; set; } = string.Empty;

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

        var message = EmailMessages.Build(_settings, toAddress, subject, htmlBody);

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

/// <summary>Builds the MIME message shared by both delivery modes.</summary>
public static class EmailMessages
{
    public static MimeMessage Build(EmailSettings settings, string toAddress, string subject, string htmlBody)
    {
        var from = !string.IsNullOrWhiteSpace(settings.FromAddress) ? settings.FromAddress
            : !string.IsNullOrWhiteSpace(settings.User) ? settings.User
            : "no-reply@daygrid.local";
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, from));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;
        message.Date = DateTimeOffset.UtcNow;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();
        return message;
    }
}

/// <summary>
/// Email:Mode = Pickup — writes every message as an RFC 822 .eml file into
/// Email:PickupDirectory instead of sending it. Used by local development and the e2e tests to
/// read verification and password-reset links without an SMTP server.
/// </summary>
public class PickupDirectoryEmailSender : IEmailSender
{
    private readonly EmailSettings _settings;
    private readonly ILogger<PickupDirectoryEmailSender> _logger;

    public PickupDirectoryEmailSender(IOptions<EmailSettings> settings, ILogger<PickupDirectoryEmailSender> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toAddress, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.PickupDirectory))
            throw new EmailNotConfiguredException("Email:Mode is Pickup but Email:PickupDirectory is not set.");

        // Relative paths resolve against the app's base directory, not the (variable) working directory.
        var directory = Path.GetFullPath(_settings.PickupDirectory, AppContext.BaseDirectory);
        Directory.CreateDirectory(directory);
        var message = EmailMessages.Build(_settings, toAddress, subject, htmlBody);

        // Timestamp first so a directory listing sorts chronologically; GUID keeps names unique.
        var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}.eml";
        var tempPath = Path.Combine(directory, fileName + ".tmp");
        await using (var stream = File.Create(tempPath))
            await message.WriteToAsync(stream, ct);
        File.Move(tempPath, Path.Combine(directory, fileName)); // readers never see a half-written file

        _logger.LogInformation("Email written to pickup directory as {FileName}", fileName);
    }
}
