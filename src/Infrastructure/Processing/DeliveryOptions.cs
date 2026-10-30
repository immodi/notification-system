namespace Infrastructure.Processing;

public sealed class DeliveryOptions
{
    public const string SectionName = "Delivery";

    public int MaxRetries { get; init; } = 3;

    public int BaseDelaySeconds { get; init; } = 1;
}
