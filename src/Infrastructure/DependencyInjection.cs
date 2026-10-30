using Application.Interfaces;
using Application.Services;
using Infrastructure.Messaging;
using Infrastructure.Persistence;
using Infrastructure.Processing;
using Infrastructure.Providers;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection")));


        services.AddSingleton(typeof(IQueue<>), typeof(InMemoryQueue<>));
        
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>)); 
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationProvider, DummyNotificationProvider>();

        services.AddHostedService<NotificationWorker>();
        
        return services;
    }
}