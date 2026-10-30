using Application.Interfaces;
using Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Processing;

public class NotificationWorker(
    ILogger<NotificationWorker> logger,
    IServiceScopeFactory scopeFactory,
    IQueue<Guid> queue,
    IOptions<DeliveryOptions> deliveryOptions) : BackgroundService
{
    private readonly DeliveryOptions _deliveryOptions = deliveryOptions.Value;

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
                    var outcome = await ProcessAsync(message.Payload, stoppingToken);

                    if (outcome is ProcessingOutcome.DeadLetter)
                        await message.RejectAsync(requeue: false, stoppingToken);
                    else
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

    private async Task<ProcessingOutcome> ProcessAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var provider = scope.ServiceProvider.GetRequiredService<INotificationProvider>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var notification = await repository.GetByIdAsync(id, cancellationToken);
        if (notification is null)
        {
            logger.LogInformation("Notification {NotificationId} not found", id);
            return ProcessingOutcome.Acknowledge;
        }

        if (notification.Status is NotificationStatus.Sent)
        {
            logger.LogInformation("Notification {NotificationId} already sent", notification.Id);
            return ProcessingOutcome.Acknowledge;
        }

        if (notification.Status is NotificationStatus.Failed
            && notification.RetryCount > _deliveryOptions.MaxRetries)
        {
            logger.LogInformation(
                "Notification {NotificationId} already exhausted its retries, moving to dead-letter queue",
                notification.Id);

            return ProcessingOutcome.DeadLetter;
        }

        logger.LogInformation("Notification {NotificationId} processing", notification.Id);
        notification.MarkAsProcessing();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await provider.SendNotificationAsync(notification, cancellationToken);
            notification.MarkAsSent();
            logger.LogInformation("Notification {NotificationId} delivered", notification.Id);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return ProcessingOutcome.Acknowledge;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var isTerminal = notification.RetryCount + 1 > _deliveryOptions.MaxRetries;
            notification.MarkAsFailed(ex.Message, isTerminal);

            logger.LogError(
                ex,
                "Notification {NotificationId} failed on attempt {Attempt} of {MaxAttempts}",
                notification.Id,
                notification.RetryCount,
                _deliveryOptions.MaxRetries + 1);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (isTerminal)
            {
                logger.LogWarning(
                    "Notification {NotificationId} moved to dead-letter queue after {RetryCount} failed attempts",
                    notification.Id,
                    notification.RetryCount);

                return ProcessingOutcome.DeadLetter;
            }

            var delay = GetRetryDelay(notification.RetryCount);
            logger.LogInformation(
                "Notification {NotificationId} retrying in {DelaySeconds}s (retry {RetryCount} of {MaxRetries})",
                notification.Id,
                delay.TotalSeconds,
                notification.RetryCount,
                _deliveryOptions.MaxRetries);

            await queue.EnqueueDelayedAsync(notification.Id, delay, cancellationToken);

            return ProcessingOutcome.Acknowledge;
        }
    }

    private TimeSpan GetRetryDelay(int retryCount)
    {
        var exponent = Math.Max(0, retryCount - 1);
        var seconds = _deliveryOptions.BaseDelaySeconds * Math.Pow(2, exponent);

        return TimeSpan.FromSeconds(seconds);
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

    private enum ProcessingOutcome
    {
        Acknowledge,
        DeadLetter
    }
}
