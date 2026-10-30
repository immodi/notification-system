using Application.DTOs;
using Application.Interfaces;
using Application.Services;
using Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace NotificationSystem.UnitTests.Application;

public class NotificationServiceTests
{
    private readonly FakeNotificationRepository _repository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly RecordingQueue _queue = new();
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        _service = new NotificationService(
            _unitOfWork,
            _repository,
            NullLogger<NotificationService>.Instance,
            _queue);
    }

    [Fact]
    public async Task Create_persists_a_queued_notification_and_publishes_its_id()
    {
        var dto = new NotificationDto("jane@example.com", "Welcome", "Thanks for signing up");

        var id = await _service.CreateAsync(dto);

        var notification = Assert.Single(_repository.Items);
        Assert.Equal(id, notification.Id);
        Assert.Equal(dto.Recipient, notification.Recipient);
        Assert.Equal(dto.Subject, notification.Subject);
        Assert.Equal(dto.Message, notification.Message);
        Assert.Equal(NotificationStatus.Queued, notification.Status);
        Assert.Equal(id, Assert.Single(_queue.Enqueued));
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Get_by_id_returns_the_stored_notification()
    {
        var id = await _service.CreateAsync(
            new NotificationDto("jane@example.com", "Welcome", "Message"));

        var notification = await _service.GetByIdAsync(id);

        Assert.NotNull(notification);
        Assert.Equal(id, notification.Id);
    }

    [Fact]
    public async Task Get_by_id_returns_null_for_an_unknown_notification()
    {
        var notification = await _service.GetByIdAsync(Guid.NewGuid());

        Assert.Null(notification);
    }

    [Fact]
    public async Task Update_persists_changes()
    {
        var id = await _service.CreateAsync(
            new NotificationDto("jane@example.com", "Welcome", "Message"));
        var notification = await _service.GetByIdAsync(id);
        Assert.NotNull(notification);

        notification.MarkAsSent();
        await _service.UpdateAsync(notification);

        Assert.Equal(2, _unitOfWork.SaveCount);
        Assert.Equal(NotificationStatus.Sent, _repository.Items.Single().Status);
    }

    [Fact]
    public async Task Delete_removes_the_notification()
    {
        var id = await _service.CreateAsync(
            new NotificationDto("jane@example.com", "Welcome", "Message"));

        await _service.DeleteAsync(id);

        Assert.Empty(_repository.Items);
        Assert.Equal(2, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Delete_of_an_unknown_notification_is_a_no_op()
    {
        await _service.DeleteAsync(Guid.NewGuid());

        Assert.Empty(_repository.Items);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Get_queued_rejects_non_positive_limits(int limit)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _service.GetQueuedAsync(limit));
    }

    private sealed class FakeNotificationRepository : INotificationRepository
    {
        public List<Notification> Items { get; } = [];

        public Task<Notification?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.SingleOrDefault(n => n.Id == id));
        }

        public Task<IReadOnlyList<Notification>> GetQueuedAsync(
            int limit,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Notification> result = Items
                .Where(n => n.Status == NotificationStatus.Queued)
                .Take(limit)
                .ToList();

            return Task.FromResult(result);
        }

        public Task AddAsync(
            Notification entity,
            CancellationToken cancellationToken = default)
        {
            Items.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(Notification entity)
        {
        }

        public void Remove(Notification entity)
        {
            Items.Remove(entity);
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingQueue : IQueue<Guid>
    {
        public List<Guid> Enqueued { get; } = [];

        public Task EnqueueAsync(
            Guid message,
            CancellationToken cancellationToken = default)
        {
            Enqueued.Add(message);
            return Task.CompletedTask;
        }

        public Task EnqueueDelayedAsync(
            Guid message,
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            Enqueued.Add(message);
            return Task.CompletedTask;
        }

        public Task<IQueueMessage<Guid>?> DequeueAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
