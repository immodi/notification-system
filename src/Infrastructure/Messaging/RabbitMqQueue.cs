using System.Text.Json;
using Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Infrastructure.Messaging;

public sealed class RabbitMqQueue<T>(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqQueue<T>> logger)
    : IQueue<T>, IAsyncDisposable
    where T : notnull
{
    private static readonly TimeSpan EmptyQueueDelay = TimeSpan.FromMilliseconds(250);

    private readonly RabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;
    private bool _disposed;

    public async Task EnqueueAsync(
        T message,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        await using var channel = await CreateChannelAsync(cancellationToken);

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json"
        };

        await channel.BasicPublishAsync(
            RabbitMqTopology.Exchange,
            RabbitMqTopology.RoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }

    public async Task<IQueueMessage<T>?> DequeueAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var channel = await CreateChannelAsync(cancellationToken);
        try
        {
            var result = await channel.BasicGetAsync(
                RabbitMqTopology.Queue,
                autoAck: false,
                cancellationToken: cancellationToken);

            if (result is null)
            {
                await channel.DisposeAsync();
                await Task.Delay(EmptyQueueDelay, cancellationToken);
                return null;
            }

            var message = JsonSerializer.Deserialize<T>(result.Body.Span)
                ?? throw new JsonException(
                    $"Could not deserialize a message of type {typeof(T).Name}.");

            return new RabbitMqQueueMessage(channel, result.DeliveryTag, message, logger);
        }
        catch
        {
            await channel.DisposeAsync();
            throw;
        }
    }

    private async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
            return _connection;

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
                return _connection;

            if (_connection is not null)
            {
                logger.LogWarning("RabbitMQ connection is closed, reconnecting");
                await _connection.DisposeAsync();
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.Username,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                ClientProvidedName = "NotificationSystem",
                AutomaticRecoveryEnabled = true
            };

            logger.LogInformation(
                "Connecting to RabbitMQ at {Host}:{Port}",
                _options.Host,
                _options.Port);

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            await DeclareTopologyAsync(_connection, cancellationToken);

            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private static async Task DeclareTopologyAsync(
        IConnection connection,
        CancellationToken cancellationToken)
    {
        await using var channel = await connection.CreateChannelAsync(
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            RabbitMqTopology.Exchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            RabbitMqTopology.Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = RabbitMqTopology.DeadLetterExchange,
                ["x-dead-letter-routing-key"] = RabbitMqTopology.DeadLetterRoutingKey
            },
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            RabbitMqTopology.Queue,
            RabbitMqTopology.Exchange,
            RabbitMqTopology.RoutingKey,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            RabbitMqTopology.DeadLetterExchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            RabbitMqTopology.DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            RabbitMqTopology.DeadLetterQueue,
            RabbitMqTopology.DeadLetterExchange,
            RabbitMqTopology.DeadLetterRoutingKey,
            cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        _connectionLock.Dispose();
    }

    private sealed class RabbitMqQueueMessage(
        IChannel channel,
        ulong deliveryTag,
        T payload,
        ILogger<RabbitMqQueue<T>> logger)
        : IQueueMessage<T>
    {
        private bool _settled;

        public T Payload { get; } = payload;

        public async Task AcknowledgeAsync(
            CancellationToken cancellationToken = default)
        {
            if (_settled)
                return;

            _settled = true;
            await channel.BasicAckAsync(
                deliveryTag,
                multiple: false,
                cancellationToken: cancellationToken);

            logger.LogDebug(
                "Acknowledged RabbitMQ message {DeliveryTag}",
                deliveryTag);
        }

        public async Task RejectAsync(
            bool requeue,
            CancellationToken cancellationToken = default)
        {
            if (_settled)
                return;

            _settled = true;
            await channel.BasicNackAsync(
                deliveryTag,
                multiple: false,
                requeue: requeue,
                cancellationToken: cancellationToken);

            logger.LogWarning(
                "Rejected RabbitMQ message {DeliveryTag} (requeue: {Requeue})",
                deliveryTag,
                requeue);
        }

        public async ValueTask DisposeAsync()
        {
            // Closing a channel with unacknowledged deliveries requeues them,
            // which gives at-least-once delivery if the worker never settles.
            await channel.DisposeAsync();
        }
    }
}
