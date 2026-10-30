using Application.Interfaces;
using Domain;

namespace NotificationSystem.UnitTests.Messaging;

public class InMemoryQueueTests
{
    [Fact]
    public async Task Enqueue_then_dequeue_returns_the_message()
    {
        var queue = new Infrastructure.Messaging.InMemoryQueue<Guid>();
        var id = Guid.NewGuid();

        await queue.EnqueueAsync(id);
        var message = await queue.DequeueAsync();

        Assert.NotNull(message);
        Assert.Equal(id, message.Payload);

        await message.AcknowledgeAsync();
        await message.DisposeAsync();
    }

    [Fact]
    public async Task Delayed_message_is_delivered_after_the_delay()
    {
        var queue = new Infrastructure.Messaging.InMemoryQueue<Guid>();
        var id = Guid.NewGuid();

        await queue.EnqueueDelayedAsync(id, TimeSpan.FromMilliseconds(50));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var message = await queue.DequeueAsync(timeout.Token);

        Assert.NotNull(message);
        Assert.Equal(id, message.Payload);
    }

    [Fact]
    public async Task Reject_without_requeue_does_not_redeliver()
    {
        var queue = new Infrastructure.Messaging.InMemoryQueue<Guid>();
        var id = Guid.NewGuid();
        await queue.EnqueueAsync(id);

        var message = await queue.DequeueAsync();
        Assert.NotNull(message);
        await message.RejectAsync(requeue: false);
        await message.DisposeAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => queue.DequeueAsync(timeout.Token));
    }
}
