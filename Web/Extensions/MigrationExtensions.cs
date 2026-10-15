using Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Web.Extensions;

public static class MigrationExtensions
{
    public static async void UseAutoMigrations(
        this WebApplication app)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }
        catch (Exception _)
        {
            using var scope = app.Services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<WebApplication>>();
            logger.LogCritical("couldn't load migrations, exiting...");
        }
    }
}