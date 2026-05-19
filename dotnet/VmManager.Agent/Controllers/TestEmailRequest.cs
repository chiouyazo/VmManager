namespace VmManager.Agent.Controllers;

public record TestEmailRequest(
    string ToAddress,
    string SmtpHost,
    int SmtpPort,
    string SmtpUsername,
    string SmtpPassword,
    string SmtpFromAddress,
    bool SmtpUseTls
);
