namespace Domain;

public class Notification
{
    private Notification() { }
    public Notification(
        string recipient,
        string subject,
        string message)
    {
        Id = Guid.NewGuid();
        Recipient = recipient;
        Subject = subject;
        Message = message;
        Status = NotificationStatus.Queued;
        CreatedAt = DateTime.UtcNow;
        RetryCount = 0;
    }

    public Guid Id { get; private set; }

    public string Recipient { get; private set; } = null!;

    public string Subject { get; private set; } = null!;

    public string Message { get; private set; } = null!;

    public NotificationStatus Status { get; private set; }

    public int RetryCount { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? FinishedAt { get; private set; }

    public string? LastError { get; private set; }

    public void MarkAsProcessing()
    {
        Status = NotificationStatus.Processing;
    }

    public void MarkAsSent()
    {
        Status = NotificationStatus.Sent;
        FinishedAt = DateTime.UtcNow;
        LastError = null;
    }

    public void MarkAsFailed(string error, bool isTerminal = false)
    {
        Status = NotificationStatus.Failed;
        RetryCount++;
        LastError = error;

        if (isTerminal)
            FinishedAt = DateTime.UtcNow;
    }
}
