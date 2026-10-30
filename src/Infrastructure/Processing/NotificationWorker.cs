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
            IQueueMessage<Guid>? message;

            try
            {
                message = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not read from the notification queue");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            if (message is null)
                continue;

            await using (message)
            {
                try
                {
                    await ProcessAsync(message.Payload, stoppingToken);
                    await message.AcknowledgeAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    await TryRejectAsync(message, requeue: true);
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Unexpected error processing notification {NotificationId}",
                        message.Payload);

                    await TryRejectAsync(message, requeue: true);
                }
            }
        }
    }

    private async Task ProcessAsync(Guid id, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var provider = scope.ServiceProvider.GetRequiredService<INotificationProvider>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var notification = await repository.GetByIdAsync(id, cancellationToken);
        if (notification is null)
        {
            logger.LogInformation("Notification {NotificationId} not found", id);
            return;
        }

        if (notification.Status is NotificationStatus.Sent)
        {
            logger.LogInformation("Notification {NotificationId} already sent", notification.Id);
            return;
        }

        logger.LogInformation("Notification {NotificationId} processing", notification.Id);
        notification.MarkAsProcessing();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await provider.SendNotificationAsync(notification, cancellationToken);
            notification.MarkAsSent();
            logger.LogInformation("Notification {NotificationId} delivered", notification.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            notification.MarkAsFailed(ex.Message);
            logger.LogError(ex, "Notification {NotificationId} failed", notification.Id);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task TryRejectAsync(IQueueMessage<Guid> message, bool requeue)
    {
        try
        {
            await message.RejectAsync(requeue, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Could not reject notification message {NotificationId}",
                message.Payload);
        }
    }
}
