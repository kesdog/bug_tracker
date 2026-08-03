using BugTracker.Api.Auth;
using BugTracker.Api.Bugs;
using BugTracker.Api.Projects;

namespace BugTracker.Api.Notifications;

public sealed class NotificationAuthorizationService(
    BugRepository bugRepository,
    ProjectAuthorizationService projectAuthorizationService)
{
    public async Task<bool> CanReadAsync(
        AuthenticatedUser principal,
        NotificationDto notification,
        CancellationToken ct)
    {
        if (notification.TicketId is null)
        {
            return true;
        }

        var ticket = await bugRepository.GetBugByIdAsync(notification.TicketId, ct);
        return ticket is not null &&
            await projectAuthorizationService.CanReadTicketAsync(principal, ticket, ct);
    }

    public async Task<IReadOnlyList<NotificationDto>> FilterReadableAsync(
        AuthenticatedUser principal,
        IReadOnlyList<NotificationDto> notifications,
        CancellationToken ct)
    {
        var readable = new List<NotificationDto>(notifications.Count);
        foreach (var notification in notifications)
        {
            if (await CanReadAsync(principal, notification, ct))
            {
                readable.Add(notification);
            }
        }

        return readable;
    }
}
