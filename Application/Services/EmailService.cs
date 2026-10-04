using CommonService.Application.Interfaces.IServices;

namespace CommonService.Application.Services;

public class EmailService : IEmailService
{
    public Task SendEmailAsync(string to, string subject, string body)
    {
        // Implementation for sending email
        //demo log to console, in real scenario, integrate with an email service provider (call SMTP/SendGrid)
        Console.WriteLine($"[Email to {to}] {subject}-{body}");
        return Task.CompletedTask;
    }
}
