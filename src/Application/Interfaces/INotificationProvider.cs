using Domain;

namespace Application.Interfaces;

public interface INotificationProvider
{   
    Task SendNotificationAsync(Notification notification, CancellationToken cancellationToken = default);
}