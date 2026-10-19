using System.Threading.Tasks;

namespace TechNovaConsole.Interfaces;

/// <summary>
/// Simple notification abstraction used to emit informational messages after operations.
/// Implementations may send emails, push notifications, or write to console for demo purposes.
///
/// Failure policy: notification failures should not imply rollback. Implementations may throw on failure,
/// but callers (services) must ensure that persistence has already succeeded before attempting notification
/// and must handle notification exceptions (for example by logging) so that a notification failure does not
/// falsely indicate that the operation was not persisted.
/// </summary>
public interface INotificationService
{
    Task NotifyAsync(string message);
}
