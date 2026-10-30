using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace NotificationSystem.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class NotificationApiCollection : ICollectionFixture<NotificationApiFactory>
{
    public const string Name = "notification-api";
}

[Collection(NotificationApiCollection.Name)]
public class NotificationApiTests(NotificationApiFactory factory)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Notification_is_accepted_stored_and_delivered()
    {
        using var client = factory.CreateClient();

        var response = await PostNotificationAsync(client, "jane@example.com");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var created = await response.Content.ReadFromJsonAsync<NotificationIdResponse>();
        Assert.NotNull(created);

        var stored = await WaitForNotificationAsync(
            client,
            created.Id,
            notification => notification.Status == "Sent");

        Assert.Equal(created.Id, stored.Id);
        Assert.Equal("jane@example.com", stored.Recipient);
        Assert.Equal("Integration test", stored.Subject);
        Assert.Equal("A notification from the test suite", stored.Message);
        Assert.Equal(0, stored.RetryCount);
        Assert.NotNull(stored.FinishedAt);
        Assert.Null(stored.LastError);
        Assert.Equal(1, factory.Provider.Attempts(created.Id));
    }

    [Fact]
    public async Task Notification_status_can_be_retrieved_and_missing_notifications_return_404()
    {
        using var client = factory.CreateClient();
        var created = await CreateNotificationAsync(client, "status@example.com");

        var response = await client.GetAsync($"/notifications/{created}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var notification = await response.Content.ReadFromJsonAsync<NotificationResponse>();
        Assert.NotNull(notification);
        Assert.Equal(created, notification.Id);

        var missing = await client.GetAsync($"/notifications/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(
            "application/problem+json",
            missing.Content.Headers.ContentType?.MediaType);

        var problem = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Validation_failure_returns_problem_details()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/notifications", new
        {
            recipient = "not-an-email",
            subject = "Subject",
            message = "Message"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Failed_notification_retries_then_moves_to_the_dead_letter_queue()
    {
        using var client = factory.CreateClient();
        var id = await CreateNotificationAsync(client, "fail@example.com");

        var failed = await WaitForNotificationAsync(
            client,
            id,
            notification => notification.Status == "Failed" && notification.RetryCount == 4,
            DefaultTimeout);

        Assert.NotNull(failed.FinishedAt);
        Assert.Equal("Simulated provider failure", failed.LastError);
        Assert.Equal(4, factory.Provider.Attempts(id));

        await WaitForDeadLetteredMessageAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Already_sent_notification_is_not_delivered_again()
    {
        using var client = factory.CreateClient();
        var id = await CreateNotificationAsync(client, "already-sent@example.com");

        await WaitForNotificationAsync(
            client,
            id,
            notification => notification.Status == "Sent");

        Assert.Equal(1, factory.Provider.Attempts(id));

        await PublishDuplicateAsync(id);
        await Task.Delay(TimeSpan.FromSeconds(2));

        var response = await client.GetAsync($"/notifications/{id}");
        var notification = await response.Content.ReadFromJsonAsync<NotificationResponse>();

        Assert.NotNull(notification);
        Assert.Equal("Sent", notification.Status);
        Assert.Equal(1, factory.Provider.Attempts(id));
    }

    private static async Task<HttpResponseMessage> PostNotificationAsync(
        HttpClient client,
        string recipient)
    {
        return await client.PostAsJsonAsync("/notifications", new
        {
            recipient,
            subject = "Integration test",
            message = "A notification from the test suite"
        });
    }

    private static async Task<Guid> CreateNotificationAsync(
        HttpClient client,
        string recipient)
    {
        var response = await PostNotificationAsync(client, recipient);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<NotificationIdResponse>();
        Assert.NotNull(created);

        return created.Id;
    }

    private static async Task<NotificationResponse> WaitForNotificationAsync(
        HttpClient client,
        Guid id,
        Func<NotificationResponse, bool> predicate,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        NotificationResponse? last = null;

        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/notifications/{id}");
            if (response.IsSuccessStatusCode)
            {
                last = await response.Content.ReadFromJsonAsync<NotificationResponse>();
                if (last is not null && predicate(last))
                    return last;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new Xunit.Sdk.XunitException(
            $"Timed out waiting for notification {id}. Last state: {JsonSerializer.Serialize(last)}");
    }

    private async Task PublishDuplicateAsync(Guid id)
    {
        var options = Options.Create(new RabbitMqOptions
        {
            Host = factory.RabbitMqHost,
            Port = factory.RabbitMqPort,
            Username = "guest",
            Password = "guest"
        });

        await using var queue = new RabbitMqQueue<Guid>(
            options,
            NullLogger<RabbitMqQueue<Guid>>.Instance);

        await queue.EnqueueAsync(id);
    }

    private async Task WaitForDeadLetteredMessageAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var count = await GetDeadLetterQueueMessageCountAsync();
            if (count > 0)
                return;

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        throw new Xunit.Sdk.XunitException(
            "Timed out waiting for a message in the dead-letter queue.");
    }

    private async Task<long> GetDeadLetterQueueMessageCountAsync()
    {
        var connectionFactory = new ConnectionFactory
        {
            HostName = factory.RabbitMqHost,
            Port = factory.RabbitMqPort,
            UserName = "guest",
            Password = "guest"
        };

        await using var connection = await connectionFactory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        var queue = await channel.QueueDeclarePassiveAsync(RabbitMqTopology.DeadLetterQueue);

        return queue.MessageCount;
    }

    private sealed record NotificationIdResponse(Guid Id);

    private sealed record NotificationResponse(
        Guid Id,
        string Recipient,
        string Subject,
        string Message,
        string Status,
        int RetryCount,
        DateTime CreatedAt,
        DateTime? FinishedAt,
        string? LastError);
}
