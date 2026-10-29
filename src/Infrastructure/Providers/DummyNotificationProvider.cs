using Application.Interfaces;
using Domain;

namespace Infrastructure.Providers;

public class DummyNotificationProvider : INotificationProvider
{
    public async Task SendNotificationAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        await Task.Delay(1000, cancellationToken);
    
        if (notification.Recipient == "fail@example.com")
        {
            const string message = "Simulated provider failure";
            throw new Exception(message);
        }
    }
}