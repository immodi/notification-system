using Domain;

namespace NotificationSystem.UnitTests.Domain;

public class NotificationTests
{
    [Fact]
    public void New_notification_starts_queued()
    {
        var notification = new Notification("jane@example.com", "Subject", "Message");

        Assert.NotEqual(Guid.Empty, notification.Id);
        Assert.Equal(NotificationStatus.Queued, notification.Status);
        Assert.Equal(0, notification.RetryCount);
        Assert.Null(notification.FinishedAt);
        Assert.Null(notification.LastError);
        Assert.InRange(
            notification.CreatedAt,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public void Mark_as_processing_changes_status()
    {
        var notification = new Notification("jane@example.com", "Subject", "Message");

        notification.MarkAsProcessing();

        Assert.Equal(NotificationStatus.Processing, notification.Status);
        Assert.Null(notification.FinishedAt);
    }

    [Fact]
    public void Mark_as_sent_sets_finished_at_and_clears_error()
    {
        var notification = new Notification("jane@example.com", "Subject", "Message");
        notification.MarkAsFailed("boom");

        notification.MarkAsSent();

        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.NotNull(notification.FinishedAt);
        Assert.Null(notification.LastError);
    }

    [Fact]
    public void Mark_as_failed_increments_retry_count_and_records_error()
    {
        var notification = new Notification("jane@example.com", "Subject", "Message");

        notification.MarkAsFailed("first failure");
        notification.MarkAsFailed("second failure");

        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Equal(2, notification.RetryCount);
        Assert.Equal("second failure", notification.LastError);
        Assert.Null(notification.FinishedAt);
    }

    [Fact]
    public void Terminal_failure_sets_finished_at()
    {
        var notification = new Notification("jane@example.com", "Subject", "Message");

        notification.MarkAsFailed("final failure", isTerminal: true);

        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Equal(1, notification.RetryCount);
        Assert.Equal("final failure", notification.LastError);
        Assert.NotNull(notification.FinishedAt);
    }
}
