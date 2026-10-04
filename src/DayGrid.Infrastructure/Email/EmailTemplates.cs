using System.Net;

namespace DayGrid.Infrastructure.Email;

/// <summary>A rendered email: subject plus HTML body.</summary>
public sealed record EmailContent(string Subject, string HtmlBody);

/// <summary>
/// Account emails. Every interpolated value is HTML-encoded; links are absolute URLs built from
/// <c>App:PublicBaseUrl</c> by the caller.
/// </summary>
public static class EmailTemplates
{
    public static EmailContent ConfirmEmail(string confirmUrl) => new(
        "Confirm your DayGrid account",
        Layout(
            Greeting(null) +
            "<p>Thanks for signing up for DayGrid. Please confirm your email address to activate your account:</p>" +
            Button(confirmUrl, "Confirm email address") +
            "<p>If you did not create a DayGrid account, you can ignore this email.</p>"));

    public static EmailContent ResetPassword(string? displayName, string resetUrl) => new(
        "Reset your DayGrid password",
        Layout(
            Greeting(displayName) +
            "<p>We received a request to reset your DayGrid password. Choose a new password here:</p>" +
            Button(resetUrl, "Reset password") +
            "<p>If you did not request a password reset, you can ignore this email — your password stays the same.</p>"));

    public static EmailContent AlreadyRegistered(string? displayName, string loginUrl, string forgotPasswordUrl) => new(
        "You already have a DayGrid account",
        Layout(
            Greeting(displayName) +
            "<p>Someone (hopefully you) tried to create a new DayGrid account with this email address, " +
            "but an account already exists for it.</p>" +
            Button(loginUrl, "Sign in") +
            $"<p>Forgot your password? <a href=\"{Encode(forgotPasswordUrl)}\">Reset it here</a>.</p>" +
            "<p>If this wasn't you, no action is needed.</p>"));

    public static EmailContent PasswordChanged(string? displayName, string forgotPasswordUrl) => new(
        "Your DayGrid password was changed",
        Layout(
            Greeting(displayName) +
            "<p>The password for your DayGrid account was just changed.</p>" +
            $"<p>If you did not do this, <a href=\"{Encode(forgotPasswordUrl)}\">reset your password</a> immediately.</p>"));

    /// <summary>"Hi {name}," — or a neutral "Hello," when no trusted name is available. Emails sent
    /// before the address is confirmed must pass null: the display name was typed by whoever
    /// registered, who may not be the mailbox owner (no attacker-controlled text in our mail).</summary>
    private static string Greeting(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? "<p>Hello,</p>" : $"<p>Hi {Encode(displayName)},</p>";

    private static string Button(string url, string label) =>
        $"<p><a href=\"{Encode(url)}\" style=\"display:inline-block;padding:10px 18px;background:#2563eb;color:#ffffff;" +
        $"text-decoration:none;border-radius:6px\">{Encode(label)}</a></p>" +
        $"<p style=\"font-size:12px;color:#6b7280\">Or paste this link into your browser:<br>{Encode(url)}</p>";

    private static string Layout(string body) =>
        "<!DOCTYPE html><html><body style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#111827\">" +
        body +
        "<p style=\"color:#6b7280\">— DayGrid</p></body></html>";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
