namespace Infrastructure.Messaging;

internal static class RabbitMqTopology
{
    public const string Exchange = "notifications";
    public const string Queue = "notifications";
    public const string RoutingKey = "notification";

    public const string DeadLetterExchange = "notifications.dlx";
    public const string DeadLetterQueue = "notifications.dlq";
    public const string DeadLetterRoutingKey = "notification.dead-letter";

    public const string RetryExchange = "notifications.retry";
    public const string RetryQueue = "notifications.retry";
    public const string RetryRoutingKey = "notification.retry";
}
