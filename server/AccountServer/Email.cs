using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AccountServer;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body);
}

/// <summary>Sends real mail through an SMTP server (settings under Account:Smtp).</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;

    public SmtpEmailSender(AccountOptions options) => _options = options.Smtp;

    public async Task SendAsync(string to, string subject, string body)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.StartTlsWhenAvailable);
        if (!string.IsNullOrEmpty(_options.User)) await client.AuthenticateAsync(_options.User, _options.Password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}

/// <summary>Development: writes each e-mail into a text file instead of sending it.</summary>
public sealed class FileEmailSender : IEmailSender
{
    private readonly string _dir;

    public FileEmailSender(AccountOptions options) => _dir = options.OutboxDir;

    public async Task SendAsync(string to, string subject, string body)
    {
        Directory.CreateDirectory(_dir);
        string safe = new string(to.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        string path = Path.Combine(_dir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{safe}.txt");
        await File.WriteAllTextAsync(path, $"To: {to}\nSubject: {subject}\n\n{body}\n");
    }
}

public static class EmailTemplates
{
    public static (string Subject, string Body) Verification(string link) =>
        ("Confirm your e-mail address", $"Welcome! Open this link to confirm your e-mail address:\n\n{link}\n\nIf you did not create an account, you can ignore this message.");

    public static (string Subject, string Body) ResetCode(string code, int minutes) =>
        ("Your password reset code", $"Your password reset code is:\n\n{code}\n\nType it into the game within {minutes} minutes. If you did not ask for it, you can ignore this message; your password has not changed.");
}
