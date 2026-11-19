using System.Net;
using System.Net.Mail;

namespace Kaon.Helpers;

public interface IEmailSender
{
    // Returns "email" when sent, "outbox" when saved as a test file (development only), or null when not sent
    Task<string?> SendAsync(string to, string subject, string htmlBody);
}

// Sends real email when an SMTP server is set in the settings:
//   Email:Smtp:Host, Email:Smtp:Port, Email:Smtp:User, Email:Smtp:Password, Email:Smtp:From, Email:Smtp:EnableSsl
// While developing on your own computer and with no SMTP set, each email is saved as a file in
// App_Data/outbox so you can open it and click the link. In production with no SMTP, nothing is sent.
public class EmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<EmailSender> _log;

    public EmailSender(IConfiguration config, IWebHostEnvironment env, ILogger<EmailSender> log)
    {
        _config = config;
        _env = env;
        _log = log;
    }

    public async Task<string?> SendAsync(string to, string subject, string htmlBody)
    {
        var host = _config["Email:Smtp:Host"];
        if (!string.IsNullOrWhiteSpace(host))
        {
            try
            {
                var user = _config["Email:Smtp:User"];
                var from = _config["Email:Smtp:From"] ?? user ?? "no-reply@kaon.local";
                using var client = new SmtpClient(host, _config.GetValue("Email:Smtp:Port", 587))
                {
                    EnableSsl = _config.GetValue("Email:Smtp:EnableSsl", true)
                };
                if (!string.IsNullOrWhiteSpace(user))
                    client.Credentials = new NetworkCredential(user, _config["Email:Smtp:Password"]);
                using var message = new MailMessage(from, to, subject, htmlBody) { IsBodyHtml = true };
                await client.SendMailAsync(message);
                return "email";
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Could not send an email.");
                return null;
            }
        }

        if (_env.IsDevelopment())
        {
            var folder = Path.Combine(_env.ContentRootPath, "App_Data", "outbox");
            Directory.CreateDirectory(folder);
            var safe = new string(to.Where(char.IsLetterOrDigit).ToArray());
            var file = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + safe + ".html");
            await File.WriteAllTextAsync(file, "<!-- To: " + to + " | Subject: " + subject + " -->\n" + htmlBody);
            return "outbox";
        }

        _log.LogWarning("Email is not set up (Email:Smtp:Host). No email was sent to {To}.", WebUtility.HtmlEncode(to));
        return null;
    }
}

public static class EmailTemplates
{
    private static string Wrap(string heading, string intro, string link, string footer) => $@"
<div style=""font-family:Arial,sans-serif;max-width:520px;margin:auto;padding:24px;color:#2B1810"">
  <h2 style=""color:#E8501F;margin:0 0 12px"">kaon</h2>
  <h3 style=""margin:0 0 12px"">{heading}</h3>
  <p>{intro}</p>
  <p><a href=""{link}"" style=""display:inline-block;background:#E8501F;color:#fff;text-decoration:none;padding:12px 24px;border-radius:999px;font-weight:bold"">Choose my password</a></p>
  <p style=""font-size:13px;color:#7a6a60"">{footer}</p>
  <p style=""font-size:12px;color:#7a6a60"">If the button does not work, copy this link into your browser:<br>{WebUtility.HtmlEncode(link)}</p>
</div>";

    public static string Invite(string name, string link, int hours) => Wrap(
        "Your Kaon account is ready",
        "Hello " + WebUtility.HtmlEncode(name) + ", the Kaon team set up your carinderia account. Choose your own password to get started.",
        link,
        "This private link works once and expires in " + hours + " hours. Nobody at Kaon can see your password.");

    public static string Reset(string name, string link, int minutes) => Wrap(
        "Reset your Kaon password",
        "Hello " + WebUtility.HtmlEncode(name) + ", we got a request to reset your password. Choose a new one below.",
        link,
        "This link works once and expires in " + minutes + " minutes. If you did not ask for this, you can ignore this email and your password stays the same.");
}
