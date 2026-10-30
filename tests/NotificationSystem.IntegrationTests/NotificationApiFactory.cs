using System.Collections.Concurrent;
using System.Globalization;
using Application.Interfaces;
using Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace NotificationSystem.IntegrationTests;

public sealed class NotificationApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4-management")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    public TestNotificationProvider Provider { get; } = new();

    public string RabbitMqHost => _rabbitMq.Hostname;

    public int RabbitMqPort => _rabbitMq.GetMappedPublicPort(5672);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _database.StartAsync(),
            _rabbitMq.StartAsync());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _database.GetConnectionString(),
                ["RabbitMq:Host"] = _rabbitMq.Hostname,
                ["RabbitMq:Port"] = _rabbitMq.GetMappedPublicPort(5672)
                    .ToString(CultureInfo.InvariantCulture),
                ["RabbitMq:Username"] = "guest",
                ["RabbitMq:Password"] = "guest",
                ["Delivery:MaxRetries"] = "3",
                ["Delivery:BaseDelaySeconds"] = "1"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationProvider>();
            services.AddSingleton<INotificationProvider>(Provider);
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _rabbitMq.DisposeAsync();
        await _database.DisposeAsync();
    }
}

public sealed class TestNotificationProvider : INotificationProvider
{
    private readonly ConcurrentDictionary<Guid, int> _attempts = new();

    public int Attempts(Guid id)
    {
        return _attempts.TryGetValue(id, out var attempts) ? attempts : 0;
    }

    public Task SendNotificationAsync(
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        _attempts.AddOrUpdate(notification.Id, 1, (_, attempts) => attempts + 1);

        if (notification.Recipient == "fail@example.com")
            throw new InvalidOperationException("Simulated provider failure");

        return Task.CompletedTask;
    }
}
