using System;
using System.Threading.Tasks;
using TechNovaConsole.Interfaces;

namespace TechNovaConsole.Notifications;

/// <summary>
/// Simple console-based notification implementation used for development/demo.
/// In production this can be swapped for an email or messaging implementation.
///
/// This implementation writes notifications to the console. A production implementation
/// would send messages to an external system (SMTP, Teams, Slack, etc.). The NotificationService
/// abstraction allows replacing this implementation without changing business logic.
///
/// Notification failure policy: callers should not assume a notification implies persistence. If NotifyAsync
/// throws, the caller should log the failure but must not roll back persistence that has already completed.
/// </summary>
public sealed class ConsoleNotificationService : INotificationService
{
    public Task NotifyAsync(string message)
    {
        Console.WriteLine($"[Notification] {message}");
        return Task.CompletedTask;
    }
}
