using AssetIQ.Api.Data;
using AssetIQ.Api.Infrastructure;

namespace AssetIQ.Api.Services;

/// <summary>
/// Sends an email. Until Microsoft 365 / SMTP is connected (product owner, 29 Oct 2026) the only implementation
/// writes the message to <c>outbound_emails</c> with status "logged"; replacing it does not change any workflow.
/// </summary>
public interface IEmailSender
{
    void Queue(AssetIqDbContext db, int? userId, string? toAddress, string subject, string body, DateTime now);
}

public sealed class LoggingEmailSender : IEmailSender
{
    public void Queue(AssetIqDbContext db, int? userId, string? toAddress, string subject, string body, DateTime now) =>
        db.OutboundEmails.Add(new OutboundEmail { UserId = userId, ToAddress = toAddress, Subject = subject, Body = body, Status = "logged", CreatedAt = now });
}

/// <summary>In-app notifications (one row per recipient) plus the same message by email. The caller saves.</summary>
public sealed class NotificationService(AssetIqDbContext db, AppClock clock, IEmailSender email)
{
    public void Notify(IEnumerable<UserAccount> recipients, string kind, string title, string body, string? link)
    {
        var now = clock.UtcNow;
        foreach (var user in recipients.DistinctBy(u => u.Id))
        {
            db.Notifications.Add(new Notification { UserId = user.Id, Kind = kind, Title = title, Body = body, Link = link, CreatedAt = now });
            email.Queue(db, user.Id, EmailOf(user), $"AssetIQ: {title}", link is null ? body : $"{body}\n\nOpen: {link}", now);
        }
    }

    /// <summary>An email only, for someone without an account (an employee acknowledging an item, HR-03).</summary>
    public void EmailOnly(string? address, string subject, string body) => email.Queue(db, null, address, subject, body, clock.UtcNow);

    /// <summary>Accounts linked to an employee use the employee's email; others have none on record yet (logged only).</summary>
    private string? EmailOf(UserAccount user) =>
        user.EmployeeId is { } id ? db.Employees.Where(e => e.Id == id).Select(e => e.Email).FirstOrDefault() : null;
}
