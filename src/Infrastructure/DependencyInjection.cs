using Application.Interfaces;
using Infrastructure.Persistence;
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
            options.UseSqlite(
                configuration.GetConnectionString("DefaultConnection")));
        
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>)); 
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}