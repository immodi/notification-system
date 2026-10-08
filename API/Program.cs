using Infrastructure;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddConsole();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpLogging(
    options =>
    {
        options.LoggingFields = HttpLoggingFields.RequestBody | HttpLoggingFields.ResponseBody;
    }
);

var app = builder.Build();
using var scope = app.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
await db.Database.MigrateAsync();

app.UseHttpLogging();

app.MapGet("/hello", () => Results.Json(new {message = "world!"}));

app.Run();