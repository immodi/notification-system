using Application.Interfaces;
using Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Processing;

public class NotificationWorker(
    ILogger<NotificationWorker> logger,
    IServiceScopeFactory scopeFactory,
    IQueue<Guid> queue) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
       while (!stoppingToken.IsCancellationRequested)
       {
           
            var id = await queue.DequeueAsync(stoppingToken);
           try
           {

               using var scope = scopeFactory.CreateScope();
               var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
               var provider = scope.ServiceProvider.GetRequiredService<INotificationProvider>();
               var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

               var notification = await repository.GetByIdAsync(id, stoppingToken);
               if (notification == null)
               {
                   logger.LogInformation("Notification {NotificationId} not found", id);
                   continue;
               }

               if (notification.Status is NotificationStatus.Sent)
               {
                   logger.LogInformation("Notification {NotificationId} already sent", notification.Id);
                   continue;
               }

               logger.LogInformation("Notification {NotificationId} processing", notification.Id);
               notification.MarkAsProcessing();
               await unitOfWork.SaveChangesAsync(stoppingToken);
               try
               {
                   await provider.SendNotificationAsync(notification, stoppingToken);
                   notification.MarkAsSent();
                   logger.LogInformation("Notification {NotificationId} delivered", notification.Id);
               }
               catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
               catch (Exception ex)
               {
                   notification.MarkAsFailed(ex.Message);
                   logger.LogError(ex, "Notification {NotificationId} failed", notification.Id);
               }

               await unitOfWork.SaveChangesAsync(stoppingToken);
           }
           catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
           catch (Exception ex)
           {
               logger.LogError(ex, "Unexpected error processing notification {NotificationId}", id);  
           }
       } 
    }
}