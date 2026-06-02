using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using VmManager.Catalog.Shared;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Services;

public class EmailService
{
    private readonly SettingsService _settingsService;
    private readonly ILogger<EmailService> _logger;

    public EmailService(SettingsService settingsService, ILogger<EmailService> logger)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(logger);
        _settingsService = settingsService;
        _logger = logger;
    }

    public bool IsConfigured
    {
        get
        {
            AppSettings settings = _settingsService.Load();
            return settings.SmtpEnabled
                && !string.IsNullOrWhiteSpace(settings.SmtpHost)
                && !string.IsNullOrWhiteSpace(settings.SmtpFromAddress);
        }
    }

    public async Task SendAsync(
        string toAddress,
        string subject,
        string htmlBody,
        string? messageId = null,
        string? inReplyTo = null
    )
    {
        if (!EmailValidator.IsValid(toAddress))
        {
            _logger.LogWarning("Skipping email to invalid address: {Address}", toAddress);
            return;
        }

        AppSettings settings = _settingsService.Load();
        if (!settings.SmtpEnabled || string.IsNullOrWhiteSpace(settings.SmtpHost))
            return;

        try
        {
            using SmtpClient client = CreateClient(settings);
            MailMessage message = new MailMessage(
                settings.SmtpFromAddress,
                toAddress,
                subject,
                htmlBody
            )
            {
                IsBodyHtml = true,
            };

            if (!string.IsNullOrEmpty(messageId))
                message.Headers.Add("Message-ID", messageId);
            if (!string.IsNullOrEmpty(inReplyTo))
            {
                message.Headers.Add("In-Reply-To", inReplyTo);
                message.Headers.Add("References", inReplyTo);
            }

            await client.SendMailAsync(message);
            _logger.LogInformation("Email sent to {Address}: {Subject}", toAddress, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send email to {Address}: {Subject}",
                toAddress,
                subject
            );
        }
    }

    public async Task<EmailTestResult> TestAsync(
        string toAddress,
        string smtpHost,
        int smtpPort,
        string smtpUsername,
        string smtpPassword,
        string smtpFromAddress,
        bool smtpUseTls
    )
    {
        if (!EmailValidator.IsValid(toAddress))
            return new EmailTestResult { Success = false, Error = "Invalid email address" };

        if (string.IsNullOrWhiteSpace(smtpHost))
            return new EmailTestResult { Success = false, Error = "SMTP host is required" };

        if (string.IsNullOrWhiteSpace(smtpFromAddress))
            return new EmailTestResult { Success = false, Error = "From address is required" };

        try
        {
            SmtpClient client = new SmtpClient(smtpHost, smtpPort) { EnableSsl = smtpUseTls };
            if (!string.IsNullOrWhiteSpace(smtpUsername))
                client.Credentials = new NetworkCredential(smtpUsername, smtpPassword);

            using (client)
            {
                MailMessage message = new MailMessage(
                    smtpFromAddress,
                    toAddress,
                    "VmManager Test Email",
                    "<h2>VmManager</h2><p>This is a test email. Your SMTP configuration is working correctly.</p>"
                )
                {
                    IsBodyHtml = true,
                };
                await client.SendMailAsync(message);
            }
            return new EmailTestResult { Success = true };
        }
        catch (Exception ex)
        {
            return new EmailTestResult { Success = false, Error = ex.Message };
        }
    }

    private static SmtpClient CreateClient(AppSettings settings)
    {
        SmtpClient client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
        {
            EnableSsl = settings.SmtpUseTls,
        };

        if (!string.IsNullOrWhiteSpace(settings.SmtpUsername))
        {
            client.Credentials = new NetworkCredential(
                settings.SmtpUsername,
                settings.SmtpPassword
            );
        }

        return client;
    }
}
